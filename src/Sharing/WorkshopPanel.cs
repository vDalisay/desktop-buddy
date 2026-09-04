using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.Ui;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Small in-game Workshop surface. Full discovery opens in the player's browser; this window owns
/// publishing, subscriptions, validation/import, and explicit application of imported rooms.
/// Remote preview images are deliberately not rendered here.
/// </summary>
public partial class WorkshopPanel : Window
{
    private WorkshopSharingCoordinator? _sharing;
    private RoomPaintingLibraryStore? _rooms;
    private IRoomPaintingSharingHost? _environment;
    private CharacterSelectionState? _selection;
    private WorkshopPreviewCapture? _previews;
    private readonly List<Button> _operationButtons = [];
    private LineEdit _title = null!;
    private TextEdit _description = null!;
    private Label _availability = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private VBoxContainer _subscriptions = null!;
    private VBoxContainer _roomLibrary = null!;
    private Button _publishBuddy = null!;
    private Button _cancel = null!;
    private Control _publishSuccessBlocker = null!;
    private PanelContainer _publishSuccessPanel = null!;
    private Label _publishSuccessMessage = null!;
    private Button _openPublishedItem = null!;
    private ulong _publishedFileId;
    private CancellationTokenSource? _activeOperation;
    private bool _built;
    private bool _busy;

    public bool IsOpen => Visible;

    public void Configure(
        WorkshopSharingCoordinator sharing,
        RoomPaintingLibraryStore rooms,
        IRoomPaintingSharingHost environment,
        CharacterSelectionState selection,
        WorkshopPreviewCapture previews)
    {
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _previews = previews ?? throw new ArgumentNullException(nameof(previews));
        _selection.Changed -= OnCharacterSelectionChanged;
        _selection.Changed += OnCharacterSelectionChanged;
        if (_built) RefreshAvailability();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Title = string.Empty;
        Size = new Vector2I(900, 700);
        MinSize = new Vector2I(720, 560);
        Borderless = true;
        Unresizable = false;
        DockWindow.ApplyOwnedWindowFlags(this);
        Theme = Win98ThemeFactory.Create();
        CloseRequested += OnCloseRequested;
        Build();
        _built = true;
        RefreshAvailability();
        Hide();
    }

    public override void _ExitTree()
    {
        if (_selection is not null) _selection.Changed -= OnCharacterSelectionChanged;
        CloseRequested -= OnCloseRequested;
        _activeOperation?.Cancel();
        _activeOperation?.Dispose();
        _activeOperation = null;
        base._ExitTree();
    }

    public void Open()
    {
        if (!_built || _sharing is null) return;
        RefreshAvailability();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        Position = usable.Position + ((usable.Size - Size) / 2);
        DockWindow.ShowOwned(this);
        _ = RefreshAsync();
    }

    private void Build()
    {
        var root = new PanelContainer { Name = "WorkshopRoot" };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        AddChild(root);

        var chrome = new VBoxContainer
        {
            Name = "WorkshopChrome",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        chrome.AddThemeConstantOverride("separation", 0);
        root.AddChild(chrome);
        chrome.AddChild(BuildTitleBar());

        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddThemeConstantOverride("margin_left", Win98ThemeFactory.Px(8));
        margin.AddThemeConstantOverride("margin_top", Win98ThemeFactory.Px(8));
        margin.AddThemeConstantOverride("margin_right", Win98ThemeFactory.Px(8));
        margin.AddThemeConstantOverride("margin_bottom", Win98ThemeFactory.Px(8));
        chrome.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(6));
        margin.AddChild(column);

        var availabilityPanel = new PanelContainer();
        availabilityPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        column.AddChild(availabilityPanel);
        _availability = new Label
        {
            Text = "Steam Workshop: checking...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
        };
        availabilityPanel.AddChild(_availability);

        var browseRow = new HBoxContainer();
        column.AddChild(browseRow);
        AddOperationButton(browseRow, "Browse Workshop...", () => _sharing?.OpenWorkshopBrowser());
        AddOperationButton(browseRow, "Refresh Subscriptions", () => _ = RefreshSubscriptionsAsync());
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        browseRow.AddChild(spacer);
        var legal = new Label
        {
            Text = "Publishing is subject to the Steam Workshop Legal Agreement.",
            VerticalAlignment = VerticalAlignment.Center,
        };
        legal.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(11));
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
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(78)),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        column.AddChild(_description);

        var publishRow = new HBoxContainer();
        column.AddChild(publishRow);
        AddOperationButton(publishRow, "Publish Room Painting", () => _ = PublishRoomAsync());
        _publishBuddy = AddOperationButton(publishRow, "Publish Active Buddy", () => _ = PublishBuddyAsync());

        var split = new HSplitContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SplitOffsets = [545],
        };
        column.AddChild(split);

        var subscriptionColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        subscriptionColumn.AddChild(SectionLabel("My Subscriptions"));
        var subscriptionScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        subscriptionColumn.AddChild(subscriptionScroll);
        _subscriptions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        subscriptionScroll.AddChild(_subscriptions);
        split.AddChild(subscriptionColumn);

        var roomColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        roomColumn.AddChild(SectionLabel("Imported Room Paintings"));
        var roomScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        roomColumn.AddChild(roomScroll);
        _roomLibrary = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        roomScroll.AddChild(_roomLibrary);
        split.AddChild(roomColumn);

        var progressRow = new HBoxContainer();
        column.AddChild(progressRow);
        _progress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(14)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        progressRow.AddChild(_progress);
        _cancel = new Button
        {
            Text = "Cancel",
            Disabled = true,
            TooltipText = "Stop waiting for the current Workshop operation. Steam may finish an already-submitted upload in the background.",
        };
        _cancel.Pressed += CancelActiveOperation;
        progressRow.AddChild(_cancel);

        var statusPanel = new PanelContainer
        {
            Name = "Win98StatusBar",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(36)),
        };
        statusPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        chrome.AddChild(statusPanel);
        _status = new Label
        {
            Text = "Ready.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        statusPanel.AddChild(_status);

        var resizeGrip = new Control
        {
            Name = "ResizeGrip",
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Fdiagsize,
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(14), Win98ThemeFactory.Px(14)),
            Size = new Vector2(Win98ThemeFactory.Px(14), Win98ThemeFactory.Px(14)),
            FocusMode = Control.FocusModeEnum.None,
        };
        resizeGrip.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        resizeGrip.Position = -resizeGrip.Size;
        resizeGrip.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                StartResize(DisplayServer.WindowResizeEdge.BottomRight);
        };
        AddChild(resizeGrip);

        var overlay = new Control { Name = "WorkshopModalOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _publishSuccessBlocker = Win98Dialog.Blocker(overlay, "WorkshopPublishSuccessBlocker");
        _publishSuccessPanel = Win98Dialog.Create(
            "WorkshopPublishSuccessDialog",
            "Published!",
            new Vector2(390, 170),
            out VBoxContainer successBody,
            HidePublishSuccess,
            draggable: false);
        overlay.AddChild(_publishSuccessPanel);
        _publishSuccessMessage = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        successBody.AddChild(_publishSuccessMessage);
        var successActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        successBody.AddChild(successActions);
        _openPublishedItem = Win98Dialog.Action(successActions, "Open Workshop Page...", OpenPublishedItem);
        _openPublishedItem.TooltipText = "Open this Steam Workshop item so you can edit its details and images.";
        Win98Dialog.Action(successActions, "Done", HidePublishSuccess);
    }

    private PanelContainer BuildTitleBar()
    {
        var titleBar = new PanelContainer
        {
            Name = "TitleBar",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(Win98ThemeFactory.TitleBarHeight)),
            MouseDefaultCursorShape = Control.CursorShape.Move,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        titleBar.GuiInput += OnTitleBarInput;

        var row = new HBoxContainer
        {
            Name = "TitleBarRow",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(Win98ThemeFactory.TitleButtonGap));
        titleBar.AddChild(row);

        var icon = new Label
        {
            Text = "▣",
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(18), 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Win98ThemeFactory.TitleLabel(icon);
        row.AddChild(icon);

        var title = new Label
        {
            Text = "Desktop Buddy Workshop",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Win98ThemeFactory.TitleLabel(title);
        title.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(14));
        row.AddChild(title);

        var close = new Button
        {
            Name = "CloseBox",
            Text = "×",
            TooltipText = "Close this window.",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Win98ThemeFactory.StyleTitleButton(close);
        close.Pressed += OnCloseRequested;
        row.AddChild(close);
        return titleBar;
    }

    private void OnTitleBarInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            StartDrag();
    }

    private async Task PublishRoomAsync()
    {
        if (_busy || _sharing is null || _environment is null || _previews is null) return;
        byte[] pixels = _environment.SnapshotRoomPaintingForSharing();
        SetStatus("Preparing room preview...");
        await RunBusyAsync(async (progress, token) =>
        {
            byte[] preview = await _previews.CaptureRoomAsync(token);
            SetStatus("Publishing room painting...");
            WorkshopPublishResult result = await _sharing.PublishRoomAsync(
                pixels,
                _title.Text,
                _description.Text,
                preview,
                progress,
                token);
            SetPublishStatus(result, "Room painting");
        });
    }

    private async Task PublishBuddyAsync()
    {
        if (_busy || _sharing is null || _previews is null || _selection?.ActiveCharacterId is not Guid id) return;
        SetStatus("Preparing buddy preview...");
        await RunBusyAsync(async (progress, token) =>
        {
            byte[] preview = await _previews.CaptureBuddyAsync(id, token);
            SetStatus("Publishing active buddy...");
            WorkshopPublishResult result = await _sharing.PublishCharacterAsync(
                id,
                _title.Text,
                _description.Text,
                preview,
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
        WorkshopSubscriptionQueryResult query;
        try
        {
            query = await _sharing.GetSubscriptionsAsync();
        }
        catch (Exception exception)
        {
            if (!_busy)
            {
                RebuildSubscriptions(Array.Empty<PublishedWorkshopItem>());
                SetStatus($"Could not refresh subscriptions: {exception.Message}");
            }
            return;
        }

        if (_busy) return;

        if (!query.IsSuccess)
        {
            RebuildSubscriptions(Array.Empty<PublishedWorkshopItem>());
            SetStatus(query.Detail ?? query.Status switch
            {
                WorkshopRemoteStatus.Unavailable => "Steam Workshop is unavailable.",
                WorkshopRemoteStatus.Cancelled => "Workshop subscription refresh cancelled.",
                _ => "Could not refresh Workshop subscriptions.",
            });
            return;
        }

        IReadOnlyList<PublishedWorkshopItem> items = query.Items;
        RebuildSubscriptions(items);
        SetStatus(items.Count == 0 ? "No subscribed Desktop Buddy items found." : $"Found {items.Count} subscribed Workshop item(s).");
    }

    private void RebuildSubscriptions(IReadOnlyList<PublishedWorkshopItem> items)
    {
        Clear(_subscriptions);
        foreach (PublishedWorkshopItem item in items)
        {
            var row = new HBoxContainer { Name = $"SubscriptionRow{item.PublishedFileId}" };
            var label = new Label
            {
                Text = item.DisplayName,
                TooltipText = $"Steam Workshop item {item.PublishedFileId}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            row.AddChild(label);
            Button open = new() { Text = "Open", TooltipText = "Open this item in your browser." };
            open.Pressed += () => _sharing?.OpenWorkshopItem(item.PublishedFileId);
            row.AddChild(open);
            Button import = new() { Text = "Import", TooltipText = "Import this item as a local Desktop Buddy copy." };
            import.Pressed += () => _ = ImportAsync(item);
            row.AddChild(import);
            Button unsubscribe = new()
            {
                Name = $"Unsubscribe{item.PublishedFileId}",
                Text = "Unsubscribe",
                TooltipText = "Stop following this Workshop item in Steam. Imported local copies are kept.",
            };
            unsubscribe.Pressed += () => _ = UnsubscribeAsync(item);
            row.AddChild(unsubscribe);
            _subscriptions.AddChild(row);
        }
    }

    private async Task UnsubscribeAsync(PublishedWorkshopItem item)
    {
        if (_busy || _sharing is null) return;
        bool refresh = false;
        await RunBusyAsync(async (_, token) =>
        {
            SetStatus($"Unsubscribing from '{item.DisplayName}'...");
            WorkshopSubscriptionChangeResult result = await _sharing.UnsubscribeAsync(item.PublishedFileId, token);
            if (result.IsSuccess)
            {
                refresh = true;
                SetStatus($"Unsubscribed from '{item.DisplayName}'. Imported local copies are unchanged.");
                return;
            }

            SetStatus(result.Detail ?? (result.Status == WorkshopRemoteStatus.Cancelled
                ? "Workshop unsubscribe cancelled."
                : "Could not unsubscribe from the Workshop item."));
        });

        // RunBusyAsync intentionally blocks refreshes while a remote operation owns the UI. Wait
        // until it has released that ownership before rebuilding the subscription list.
        if (refresh)
            await RefreshSubscriptionsAsync();
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
                case WorkshopImportStatus.Cancelled:
                    SetStatus(result.Detail ?? "Workshop import was cancelled.");
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
                TooltipText = "Local imported copy. It remains available if you unsubscribe from the Workshop item.",
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
        var cancellation = new CancellationTokenSource();
        _activeOperation = cancellation;
        SetBusy(true);
        _progress.Value = 0;
        var progress = new Progress<WorkshopTransferProgress>(OnProgress);
        try
        {
            await operation(progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Workshop operation cancelled.");
        }
        catch (Exception exception)
        {
            SetStatus($"Workshop operation failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_activeOperation, cancellation)) _activeOperation = null;
            cancellation.Dispose();
            SetBusy(false);
        }
    }

    private void CancelActiveOperation()
    {
        if (!_busy || _activeOperation is null || _activeOperation.IsCancellationRequested) return;
        SetStatus("Cancelling Workshop operation...");
        _cancel.Disabled = true;
        _activeOperation.Cancel();
    }

    private void OnCloseRequested()
    {
        CancelActiveOperation();
        HidePublishSuccess();
        Hide();
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
        if (result.Status == WorkshopPublishStatus.Published)
            ShowPublishSuccess(noun, result.PublishedFileId);
    }

    private void ShowPublishSuccess(string noun, ulong publishedFileId)
    {
        _publishedFileId = publishedFileId;
        _publishSuccessMessage.Text = $"{noun} published successfully. Open its Steam Workshop page to edit the title, description, or images.";
        _publishSuccessBlocker.Visible = true;
        _publishSuccessPanel.Visible = true;
        Callable.From(_openPublishedItem.GrabFocus).CallDeferred();
    }

    private void HidePublishSuccess()
    {
        if (GodotObject.IsInstanceValid(_publishSuccessBlocker)) _publishSuccessBlocker.Visible = false;
        if (GodotObject.IsInstanceValid(_publishSuccessPanel)) _publishSuccessPanel.Visible = false;
    }

    private void OpenPublishedItem()
    {
        if (_publishedFileId != 0) _sharing?.OpenWorkshopItem(_publishedFileId);
        HidePublishSuccess();
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
        if (GodotObject.IsInstanceValid(_cancel))
            _cancel.Disabled = !_busy || _activeOperation is null || _activeOperation.IsCancellationRequested;
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
        label.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(13));
        return label;
    }

    private static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren()) child.QueueFree();
    }
}
