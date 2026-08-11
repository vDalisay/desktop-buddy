using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Persistence;
using DesktopBuddy.Ui;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentDecorator : CanvasLayer
{
    /// <summary>Synthetic catalogue tile: not a purchasable decoration, it just clears the wallpaper.</summary>
    private const string NoWallpaperId = "wallpaper.none";

    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private LabPointerGrabComponent _pointer = null!;
    private EnvironmentProgressState _state = null!;
    private SaveCoordinator _saves = null!;
    private EnvironmentDecorationLayer _visuals = null!;
    private EnvironmentEditSession? _session;
    private EnvironmentPlacementController _placement = null!;
    private Control _blocker = null!;
    private PanelContainer _panel = null!;
    private Win98PinnablePanel _pinnable = null!;
    private Win98CategoryStrip _categories = null!;
    private Label _catalogTitle = null!;
    private Win98CatalogGrid _catalogue = null!;
    private TextureRect _selectionPreview = null!;
    private Label _selectionLabel = null!;
    private Win98ValuePanel _values = null!;
    private Button _buy = null!;
    private Button _place = null!;
    private Button _move = null!;
    private Button _delete = null!;
    private Button _rotateLeft = null!;
    private Button _rotateRight = null!;
    private CheckBox _snap = null!;
    private OptionButton _grid = null!;
    private Label _status = null!;
    private PanelContainer _confirm = null!;
    private PanelContainer _placementChrome = null!;
    private Label _placementStatus = null!;
    private Button _placementDone = null!;
    private PanelContainer _moveChrome = null!;
    private PanelContainer _deleteChrome = null!;
    private readonly List<Control> _placementHiddenUi = [];
    private EnvironmentDecorationResource? _selectedDefinition;
    private PlacedDecorationId _selectedInstance;
    private bool _moveMode;
    private bool _moveDragging;
    private bool _moveHeld;
    private Vector2 _moveGrabOffset;
    private EnvironmentEditCheckpoint? _moveBaseline;
    private bool _deleteMode;
    private EnvironmentEditCheckpoint? _deleteBaseline;
    private bool _saving;
    private bool _pointerInputBefore;
    private bool _pointerUnhandledBefore;
    private bool _placementMode;
    private PlacedDecorationId _placementStagedInstance;
    private bool _panelPositioned;
    private Vector2 _lastViewportSize;
    private RoomScreenBounds _placementBounds;
    private DecorationCategory _selectedCategory;

    public bool IsOpen => GodotObject.IsInstanceValid(_blocker) && _blocker.Visible;
    internal EnvironmentLayout VisibleWorkingLayout => _session?.WorkingLayout ?? _state.Layout;
    internal long VisibleProjectedBalance
    {
        get
        {
            if (_session is not null && _session.TryProjectBalance(_progress.BalanceMilliCredits, out long projected)) return projected;
            return _progress.BalanceMilliCredits;
        }
    }
    internal bool PlacementMode => _placementMode;
    internal bool MoveMode => _moveMode;
    internal bool DeleteMode => _deleteMode;
    internal long VisibleAvailableBalance => VisibleProjectedBalance;
    internal int VisibleOwnedCount(DecorationDefinitionId id) => Owned(id);

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        LabPointerGrabComponent pointer,
        CanvasItem buddy2D,
        Node3D buddy3D,
        EnvironmentProgressState state,
        SaveCoordinator saves,
        EnvironmentDecorationLayer visuals)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        _ = buddy2D ?? throw new ArgumentNullException(nameof(buddy2D));
        _ = buddy3D ?? throw new ArgumentNullException(nameof(buddy3D));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _visuals = visuals ?? throw new ArgumentNullException(nameof(visuals));
    }

    public override void _Ready()
    {
        Layer = 115;
        ProcessMode = ProcessModeEnum.Always;
        Build();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        Rect2 room = RoomRect();
        if (!_placementMode && room.Size.X > 0 && room.Size.Y > 0) _placement.UpdateRoom(ToBounds(room));
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Clamp(viewport.X - 24, 360, 680);
        float height = Mathf.Clamp(viewport.Y - 98, 260, 620);
        if (!_pinnable.IsFloating)
            _panel.Size = new Vector2(width, height);
        if (!_panelPositioned)
        {
            _panel.Position = new Vector2(12, Mathf.Max(74, room.Position.Y + 6));
            _panelPositioned = true;
        }
        else if (!_lastViewportSize.IsEqualApprox(viewport))
        {
            _panel.Position = new Vector2(
                Mathf.Clamp(_panel.Position.X, 0, Mathf.Max(0, viewport.X - 40)),
                Mathf.Clamp(_panel.Position.Y, 0, Mathf.Max(0, viewport.Y - 30)));
        }
        _lastViewportSize = viewport;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_placementMode) CancelPlacement();
        else if (_moveMode) CancelMoveMode();
        else if (_deleteMode) CancelDeleteMode();
        else if (_placement.Active || _selectedInstance != default) CancelTransient();
        else RequestClose();
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        if (IsOpen || _saving) return;
        _session = new EnvironmentEditSession(_state.Layout, _progress.BalanceMilliCredits,
            EnvironmentDecorationRegistry.Domain, null, _state.OwnedUnplaced);
        _placement.Configure(_session, _blocker, ToBounds(RoomRect()));
        _placement.SetProcessUnhandledInput(false);
        _pointerInputBefore = _pointer.IsProcessingInput();
        _pointerUnhandledBefore = _pointer.IsProcessingUnhandledInput();
        _pointer.SetProcessInput(false);
        _pointer.SetProcessUnhandledInput(false);
        _blocker.Visible = true;
        _panel.Visible = true;
        _confirm.Visible = false;
        _selectedDefinition = null;
        _selectedInstance = default;
        _moveMode = false;
        _deleteMode = false;
        _moveDragging = false;
        _placementMode = false;
        _placementStagedInstance = default;
        ApplySavedEnvironmentPreferences();
        DecorationCategory[] categories = VisibleCategories();
        if (categories.Length > 0) SelectCategory(categories[0].ToString());
        _visuals.Preview(_session.WorkingLayout);
        _status.Text = categories.Length > 0
            ? "Select an item. Buy adds a copy to storage; Place uses an owned copy."
            : "No released room decorations are available.";
        Refresh();
    }

    private void Build()
    {
        _blocker = new Control { Name = "EnvironmentDecoratorInputSurface", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_blocker);
        _blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _blocker.GuiInput += OnRoomInput;

        _placement = new EnvironmentPlacementController { Name = nameof(EnvironmentPlacementController), ProcessMode = ProcessModeEnum.Always };
        AddChild(_placement);
        _placement.PlacementCommitted += result =>
        {
            if (result.Succeeded && _session is not null)
            {
                _placementStagedInstance = result.InstanceId;
                _placement.Cancel();
                _visuals.Preview(_session.WorkingLayout);
                _placementStatus.Text = "Position staged. Keep Placement returns to the catalogue; Cancel removes this staged copy.";
                _placementDone.Disabled = false;
            }
            else _placementStatus.Text = result.Status.ToString();
            Refresh();
        };

        _panel = Win98Dialog.Create(
            "EnvironmentDecoratorPanel",
            "Room Decorator",
            new Vector2(620, 370),
            out VBoxContainer body,
            RequestClose,
            draggable: false);
        _panel.CustomMinimumSize = new Vector2(360, 260);
        _blocker.AddChild(_panel);
        _panel.Visible = true;
        if (_panel.FindChild("CloseBox", true, false) is Button close)
            close.Name = "EnvironmentDecoratorCloseButton";
        _pinnable = new Win98PinnablePanel { Name = "RoomDecoratorPinController" };
        AddChild(_pinnable);
        _pinnable.Configure(_panel, new Vector2I(760, 620), "RoomDecoratorWindow");

        VBoxContainer panelBody = body;
        var scroll = new ScrollContainer
        {
            Name = "RoomDecoratorScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(scroll);
        var content = new VBoxContainer
        {
            Name = "RoomDecoratorContent",
            CustomMinimumSize = new Vector2(620, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(content);
        body = content;

        _categories = new Win98CategoryStrip { Name = "EnvironmentCategories" };
        _categories.SetItems(VisibleCategories().Select(category =>
            new Win98CategoryPresentation(category.ToString(), CategoryLabel(category), Tooltip: $"Browse {CategoryLabel(category).ToLowerInvariant()}.")));
        _categories.SelectionChanged += SelectCategory;
        body.AddChild(_categories);

        _catalogTitle = new Label { Name = "EnvironmentCatalogTitle", Text = "Catalog" };
        body.AddChild(_catalogTitle);
        _catalogue = new Win98CatalogGrid { Name = "EnvironmentCatalog", CustomMinimumSize = new Vector2(0, 150) };
        _catalogue.ConfigureTileSize(116, 132);
        _catalogue.SelectionChanged += SelectDefinition;
        body.AddChild(_catalogue);

        var selected = new HBoxContainer { Name = "EnvironmentSelectedItem" };
        selected.AddThemeConstantOverride("separation", 8);
        body.AddChild(selected);
        _selectionPreview = new TextureRect
        {
            Name = "EnvironmentSelectedPreview",
            CustomMinimumSize = new Vector2(52, 52),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        selected.AddChild(_selectionPreview);
        _selectionLabel = new Label
        {
            Name = "EnvironmentSelectedLabel",
            Text = "Select a catalogue item or a placed decoration.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        selected.AddChild(_selectionLabel);

        // Keep the old controls instantiated for preference migration, but user testing removed
        // snap/grid from the public demo UX. ApplySavedEnvironmentPreferences forces free placement.
        var placementPreferences = new HBoxContainer { Name = "EnvironmentPlacementPreferences", Visible = false };
        body.AddChild(placementPreferences);
        _snap = new CheckBox { Text = "Snap to grid", Visible = false };
        _snap.Toggled += OnSnapPreferenceChanged;
        placementPreferences.AddChild(_snap);
        _grid = new OptionButton { Disabled = true, Visible = false, CustomMinimumSize = new Vector2(90, 28) };
        foreach (EnvironmentGridSize size in Enum.GetValues<EnvironmentGridSize>()) _grid.AddItem(size.ToString(), (int)size);
        _grid.ItemSelected += OnGridPreferenceChanged;
        placementPreferences.AddChild(_grid);

        _values = new Win98ValuePanel { Name = "EnvironmentBudget" };
        body.AddChild(_values);
        _status = new Label { Name = "EnvironmentDecoratorStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(_status);
        var actions = new HBoxContainer { Name = "EnvironmentRoomActions" };
        actions.AddThemeConstantOverride("separation", 6);
        panelBody.AddChild(actions);
        _move = Action(actions, "Edit mode", BeginMoveMode);
        _move.Name = "EnvironmentMoveItemsButton";
        _move.TooltipText = "Select, drag and rotate items already in the room.";
        _delete = Action(actions, "Delete mode", BeginDeleteMode);
        _delete.Name = "EnvironmentDeleteItemsButton";
        _delete.TooltipText = "Enter delete mode, then click room items to return them to storage.";
        Win98Dialog.Action(actions, "Reset Room", ResetRoom).Name = "EnvironmentResetAllButton";
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _buy = Action(actions, "Buy", BuySelected);
        _buy.CustomMinimumSize = new Vector2(96, 30);
        _buy.TooltipText = "Buy one copy and keep it in storage.";
        _place = Action(actions, "Place", BeginPlacement);
        _place.CustomMinimumSize = new Vector2(96, 30);
        _place.TooltipText = "Place one owned copy anywhere inside the room.";

        _confirm = Win98Dialog.Create("EnvironmentDecoratorUnsaved", "Satisfied with your room?", new Vector2(390, 180), out VBoxContainer confirmBody);
        _blocker.AddChild(_confirm);
        confirmBody.AddChild(new Label
        {
            Text = "Save the complete room as it looks now, or revert all changes from this editing session?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var confirmActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        confirmBody.AddChild(confirmActions);
        Win98Dialog.Action(confirmActions, "Save Room", Save).Name = "EnvironmentConfirmSaveButton";
        Win98Dialog.Action(confirmActions, "Revert Room", Discard).Name = "EnvironmentDiscardButton";
        Win98Dialog.Action(confirmActions, "Keep Editing", () => _confirm.Visible = false).Name = "EnvironmentKeepEditingButton";

        _placementChrome = new PanelContainer
        {
            Name = "EnvironmentPlacementChrome",
            Visible = false,
            Theme = Win98ThemeFactory.Create(),
        };
        _placementChrome.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _placementChrome.OffsetLeft = -320;
        _placementChrome.OffsetTop = 12;
        _placementChrome.OffsetRight = -12;
        _placementChrome.OffsetBottom = 124;
        _blocker.AddChild(_placementChrome);
        var placementBody = new VBoxContainer();
        _placementChrome.AddChild(placementBody);
        _placementStatus = new Label { Text = "Click anywhere inside the room.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        placementBody.AddChild(_placementStatus);
        var placementActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        placementBody.AddChild(placementActions);
        _placementDone = Win98Dialog.Action(placementActions, "Keep Placement", ConfirmPlacement);
        _placementDone.Name = "EnvironmentPlacementDoneButton";
        Win98Dialog.Action(placementActions, "Cancel", CancelPlacement).Name = "EnvironmentPlacementCancelButton";

        _moveChrome = FocusChrome("EnvironmentMoveChrome", "Edit mode",
            "Click an item to select it. Hold the left button to drag it. Rotate the selected item below.",
            out VBoxContainer moveBody, out HBoxContainer moveActions);
        var selectionActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        moveBody.AddChild(selectionActions);
        moveBody.MoveChild(selectionActions, moveActions.GetIndex());
        _rotateLeft = Win98Dialog.Action(selectionActions, "Rotate left", () => RotateSelected(-1));
        _rotateLeft.Name = "EnvironmentRotateLeftButton";
        _rotateRight = Win98Dialog.Action(selectionActions, "Rotate right", () => RotateSelected(1));
        _rotateRight.Name = "EnvironmentRotateRightButton";
        Win98Dialog.Action(moveActions, "Done", ConfirmMoveMode).Name = "EnvironmentMoveDoneButton";
        Win98Dialog.Action(moveActions, "Cancel", CancelMoveMode).Name = "EnvironmentMoveCancelButton";

        _deleteChrome = FocusChrome("EnvironmentDeleteChrome", "Delete mode",
            "Click any room item to remove it. Purchased copies return to storage and can be placed again for free.",
            out _, out HBoxContainer deleteActions);
        Win98Dialog.Action(deleteActions, "Done", ConfirmDeleteMode).Name = "EnvironmentDeleteDoneButton";
        Win98Dialog.Action(deleteActions, "Cancel", CancelDeleteMode).Name = "EnvironmentDeleteCancelButton";
    }

    private void SelectCategory(string id)
    {
        if (!Enum.TryParse(id, out DecorationCategory category)) return;
        _selectedCategory = category;
        _catalogTitle.Text = $"Catalog - {CategoryLabel(category)}";
        CancelUnusedReservation();
        _placement.Cancel();
        _selectedDefinition = null;
        _selectedInstance = default;
        var items = new List<Win98CatalogItemPresentation>();
        if (category == DecorationCategory.Wallpaper)
            items.Add(new Win98CatalogItemPresentation(NoWallpaperId, "None", "Free",
                Tooltip: "Remove the room wallpaper. The change is staged until Save Room."));
        foreach (EnvironmentDecorationResource resource in EnvironmentDecorationRegistry.Authored.Entries)
        {
            DecorationDefinition definition = resource.ToDefinition();
            if (!definition.Visible || definition.Category != category) continue;
            items.Add(CatalogPresentation(resource));
        }
        _catalogue.SetItems(items);
        _status.Text = "Select an item. Buy adds a copy to storage; Place uses an owned copy.";
        Refresh();
    }

    private void SelectDefinition(string id)
    {
        if (id == NoWallpaperId) { RemoveWallpaper(); return; }
        if (!DecorationDefinitionId.TryCreate(id, out DecorationDefinitionId definitionId)) return;
        if (_session?.HasReservation == true && _session.ReservedDefinitionId != definitionId) _session.CancelReservation();
        _selectedDefinition = EnvironmentDecorationRegistry.Find(definitionId);
        _selectedInstance = default;
        _placement.Cancel();
        _status.Text = _selectedDefinition is null ? "Item unavailable."
            : _session?.OwnedUnplacedCount(definitionId) > 0
                ? "You own a copy in storage. Place it anywhere in the room."
                : "Buy this item before placing it.";
        Refresh();
    }

    private void BuySelected()
    {
        if (_selectedDefinition is null || _session is null) return;
        DecorationDefinition definition = _selectedDefinition.ToDefinition();
        EnvironmentEditResult result = _session.Buy(definition.Id, _progress.BalanceMilliCredits);
        _status.Text = result.Succeeded
            ? "Bought one copy. It is in storage and ready to Place."
            : result.Status == EnvironmentEditStatus.InsufficientFunds
                ? "Not enough current funds to buy this item."
                : result.Status.ToString();
        Refresh();
    }

    private void RemoveWallpaper()
    {
        CancelUnusedReservation();
        _placement.Cancel();
        _selectedDefinition = null;
        _selectedInstance = default;
        PlacedDecoration wallpaper = _session?.WorkingLayout.Decorations
            .FirstOrDefault(item => item.RenderBand == DecorationRenderBand.Wallpaper) ?? default;
        _status.Text = wallpaper.InstanceId != default && _session!.Remove(wallpaper.InstanceId).Succeeded
            ? "Wallpaper removal staged. Close the Room Decorator when satisfied to save or revert."
            : "The room has no wallpaper.";
        PreviewRoom();
        Refresh();
    }

    private void ResetRoom()
    {
        if (_session is null) return;
        CancelUnusedReservation();
        _placement.Cancel();
        _selectedDefinition = null;
        _selectedInstance = default;
        foreach (PlacedDecoration item in _session.WorkingLayout.Decorations.ToArray())
            _session.Remove(item.InstanceId);
        PreviewRoom();
        _status.Text = "Room reset. Every purchased item stays owned; close the window to save or revert.";
        Refresh();
    }

    private void BeginPlacement()
    {
        if (_selectedDefinition is null || _session is null) return;
        DecorationDefinition definition = _selectedDefinition.ToDefinition();
        if (_session.OwnedUnplacedCount(definition.Id) <= 0)
        {
            _status.Text = "Buy this item before placing it.";
            Refresh();
            return;
        }
        if (_session.HasReservation && _session.ReservedDefinitionId != definition.Id)
            _session.CancelReservation();
        if (!_session.HasReservation)
        {
            EnvironmentEditResult reserved = _session.Reserve(definition.Id, _progress.BalanceMilliCredits);
            if (!reserved.Succeeded)
            {
                _status.Text = reserved.Status == EnvironmentEditStatus.InsufficientFunds
                    ? "Not enough current funds to place this copy."
                    : reserved.Status.ToString();
                Refresh();
                return;
            }
        }

        _selectedInstance = default;
        _placementMode = true;
        _placementStagedInstance = default;
        _panel.Visible = false;
        _placementBounds = ToBounds(RoomRect());
        HideOtherUiForFocus();
        _placementChrome.Visible = true;
        _placementDone.Disabled = true;
        _placementStatus.Text = definition.RenderBand == DecorationRenderBand.Wallpaper
            ? "Click the room to stage this wallpaper."
            : "Click anywhere inside the room to stage this copy.";
        _placement.Begin(_selectedDefinition);
        _placement.UpdateRoom(_placementBounds);
        Refresh();
    }

    private void BeginMoveMode()
    {
        if (_session is null || !_session.WorkingLayout.Decorations.Any(item => item.RenderBand != DecorationRenderBand.Wallpaper)) return;
        CancelUnusedReservation();
        _moveBaseline = _session.Checkpoint();
        _moveMode = true;
        _moveDragging = false;
        _moveHeld = false;
        _selectedInstance = default;
        _panel.Visible = false;
        HideOtherUiForFocus();
        _moveChrome.Visible = true;
        Refresh();
    }

    private void BeginDeleteMode()
    {
        if (_session is null || _session.WorkingLayout.Decorations.Count == 0) return;
        CancelUnusedReservation();
        _placement.Cancel();
        _deleteBaseline = _session.Checkpoint();
        _deleteMode = true;
        _selectedInstance = default;
        _panel.Visible = false;
        HideOtherUiForFocus();
        _deleteChrome.Visible = true;
        _status.Text = "Delete mode: click items to remove them; Done keeps the staged deletions, Cancel restores them.";
        Refresh();
    }

    private void RotateSelected(int direction)
    {
        if (_session is null || _selectedInstance == default) return;
        EnvironmentEditResult result = _session.Rotate(_selectedInstance, direction);
        if (result.Succeeded && TryFindPlaced(_selectedInstance, out PlacedDecoration rotated))
            _placement.SetGhostRotationDegrees(-rotated.RotationDegrees);
        PreviewRoom();
        _status.Text = result.Succeeded ? "Item rotated." :
            result.Status == EnvironmentEditStatus.RotationNotAllowed ? "This decoration has a fixed orientation." : result.Status.ToString();
        Refresh();
    }

    private void DeleteAt(Vector2 screen)
    {
        if (_session is null || !EnvironmentPlacement.TryMap(screen.X, screen.Y, ToBounds(RoomRect()),
            DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition point))
            return;
        if (!_visuals.TryHit(point, out PlacedDecorationId id))
        {
            _status.Text = "Delete mode: click directly on an item.";
            return;
        }
        EnvironmentEditResult result = _session.Remove(id);
        if (result.Succeeded)
        {
            _status.Text = "Item removed from the room. Purchased copies remain owned in storage.";
            PreviewRoom();
        }
        else _status.Text = result.Status.ToString();
        Refresh();
    }

    private void OnRoomInput(InputEvent input)
    {
        if (_session is null || _confirm.Visible) { _blocker.AcceptEvent(); return; }
        if (!_pinnable.IsFloating && !_placementMode && !_moveMode && !_deleteMode && !_placement.Active &&
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } outside &&
            !_panel.GetGlobalRect().HasPoint(outside.GlobalPosition))
        {
            RequestClose();
            _blocker.AcceptEvent();
            return;
        }
        if (_placementMode && !_placement.Active)
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } resume)
                ResumeStagedPositioning(resume.Position);
            _blocker.AcceptEvent();
            return;
        }
        switch (input)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when _deleteMode:
                DeleteAt(click.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when _moveMode:
                SelectForEditing(click.Position);
                break;
            case InputEventMouseMotion motion when _moveMode && _moveHeld && _placement.Active:
                _moveDragging = true;
                _placement.UpdatePointer(motion.Position + _moveGrabOffset);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } release when _moveMode:
                _moveHeld = false;
                if (_moveDragging) DropCarried(release.Position);
                break;
            case InputEventMouseMotion motion when _placement.Active && !_moveMode:
                _placement.UpdatePointer(motion.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
                when _placement.Active && _placementMode && _placementStagedInstance != default:
                DropStaged(click.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when _placement.Active:
                _placement.UpdatePointer(click.Position);
                _placement.CommitGhost();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                SelectPlaced(click.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                if (_moveMode) CancelMoveMode();
                else if (_deleteMode) CancelDeleteMode();
                else CancelTransient();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                if (_moveMode) CancelMoveMode();
                else if (_deleteMode) CancelDeleteMode();
                else if (_placement.Active || _selectedInstance != default) CancelTransient();
                else RequestClose();
                break;
        }
        _blocker.AcceptEvent();
    }

    private void ResumeStagedPositioning(Vector2 screen)
    {
        if (_session is null || _placementStagedInstance == default) return;
        if (!TryFindPlaced(_placementStagedInstance, out PlacedDecoration staged)) return;
        if (EnvironmentDecorationRegistry.Find(staged.DefinitionId) is not EnvironmentDecorationResource resource) return;
        if (staged.RenderBand == DecorationRenderBand.Wallpaper) return;
        if (!EnvironmentPlacement.TryMap(screen.X, screen.Y, ToBounds(RoomRect()), DecorationAnchorKind.RoomSurface,
            false, EnvironmentGridSize.Medium, out CanonicalRoomPosition point)) return;
        if (!_visuals.TryHit(point, out PlacedDecorationId hit) || hit != _placementStagedInstance) return;

        _selectedInstance = _placementStagedInstance;
        _placement.Begin(resource);
        _placement.UpdatePointer(screen);
        _placement.SetGhostRotationDegrees(-staged.RotationDegrees);
        PreviewRoom();
        _placementStatus.Text = "Click a new spot for this copy.";
    }

    private void DropStaged(Vector2 screen)
    {
        if (_session is null || !_placement.UpdatePointer(screen)) return;
        _session.Move(_placementStagedInstance, _placement.GhostPosition);
        _selectedInstance = default;
        _placement.Cancel();
        PreviewRoom();
        _placementStatus.Text = "Position staged. Keep Placement returns to the catalogue.";
        Refresh();
    }

    private void SelectForEditing(Vector2 screen)
    {
        _moveDragging = false;
        _moveHeld = false;
        _placement.Cancel();
        SelectPlaced(screen);
        if (_session is null || !TryFindPlaced(_selectedInstance, out PlacedDecoration carried))
        {
            PreviewRoom();
            Refresh();
            return;
        }
        if (carried.RenderBand == DecorationRenderBand.Wallpaper)
        {
            _status.Text = "Wallpaper fills the room and cannot be moved. Replace or clear it from Wallpapers, or use Delete mode.";
            PreviewRoom();
            Refresh();
            return;
        }
        if (EnvironmentDecorationRegistry.Find(carried.DefinitionId) is not EnvironmentDecorationResource resource) return;
        (float x, float y) = EnvironmentPlacement.ToScreen(carried.Position, ToBounds(RoomRect()));
        _moveGrabOffset = new Vector2(x, y) - screen;
        _moveHeld = true;
        _placement.Begin(resource);
        _placement.UpdatePointer(screen + _moveGrabOffset);
        _placement.SetGhostRotationDegrees(-carried.RotationDegrees);
        PreviewRoom();
        _status.Text = "Item selected. Hold the left button to drag it, or use Rotate.";
        Refresh();
    }

    private void DropCarried(Vector2 screen)
    {
        if (_session is null || _selectedInstance == default) return;
        if (!_placement.UpdatePointer(screen + _moveGrabOffset)) return;
        _session.Move(_selectedInstance, _placement.GhostPosition);
        _moveDragging = false;
        PreviewRoom();
        _status.Text = "Item dropped. It stays selected for rotation.";
        Refresh();
    }

    private void PreviewRoom()
    {
        if (_session is null) return;
        EnvironmentLayout working = _session.WorkingLayout;
        if (!_placement.Active || _selectedInstance == default)
        {
            _visuals.Preview(working);
            return;
        }
        _visuals.Preview(
            new EnvironmentLayout(working.Decorations.Where(item => item.InstanceId != _selectedInstance)),
            working);
    }

    private void SelectPlaced(Vector2 screen)
    {
        if (_session is null || !EnvironmentPlacement.TryMap(screen.X, screen.Y, ToBounds(RoomRect()),
            DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition point)) return;
        if (_visuals.TryHit(point, out PlacedDecorationId id))
        {
            _selectedInstance = id;
            _selectedDefinition = null;
            _status.Text = "Placed item selected. Use Edit mode to move/rotate it or Delete mode to remove it.";
        }
        else _selectedInstance = default;
        Refresh();
    }

    private void CancelTransient()
    {
        _placement.Cancel();
        _selectedInstance = default;
        PreviewRoom();
        Refresh();
    }

    private void ConfirmPlacement()
    {
        if (!_placementMode || _placementStagedInstance == default) return;
        PlacedDecorationId placed = _placementStagedInstance;
        EnvironmentDecorationResource? justPlaced = _selectedDefinition;
        EndPlacementMode();
        _selectedDefinition = justPlaced;
        _selectedInstance = placed;
        _status.Text = "Placement kept. Buy another copy or close the Room Decorator when satisfied.";
        Refresh();
    }

    private void CancelPlacement()
    {
        if (!_placementMode || _session is null) return;
        if (_placementStagedInstance != default) _session.RemoveStaged(_placementStagedInstance);
        else if (_session.HasReservation) _session.CancelReservation();
        EndPlacementMode();
        _status.Text = "Placement cancelled; the staged cost was restored.";
        PreviewRoom();
        Refresh();
    }

    private void EndPlacementMode()
    {
        _placement.Cancel();
        _placementMode = false;
        _placementStagedInstance = default;
        _selectedInstance = default;
        _placementChrome.Visible = false;
        RestorePlacementUi();
        _panel.Visible = true;
    }

    private void ConfirmMoveMode()
    {
        EndMoveMode();
        PreviewRoom();
        _status.Text = "Room edits staged. Close the Room Decorator when satisfied.";
        Refresh();
    }

    private void CancelMoveMode()
    {
        if (_session is not null && _moveBaseline is not null) _session.Restore(_moveBaseline);
        EndMoveMode();
        PreviewRoom();
        _status.Text = "Edit mode cancelled; the room is back as it was before that mode.";
        Refresh();
    }

    private void EndMoveMode()
    {
        _placement.Cancel();
        _moveMode = false;
        _moveDragging = false;
        _moveHeld = false;
        _selectedInstance = default;
        _moveBaseline = null;
        _moveChrome.Visible = false;
        RestorePlacementUi();
        _panel.Visible = true;
    }

    private void ConfirmDeleteMode()
    {
        EndDeleteMode();
        PreviewRoom();
        _status.Text = "Deletions staged. Close the Room Decorator when satisfied.";
        Refresh();
    }

    private void CancelDeleteMode()
    {
        if (_session is not null && _deleteBaseline is not null) _session.Restore(_deleteBaseline);
        EndDeleteMode();
        PreviewRoom();
        _status.Text = "Delete mode cancelled; removed items were restored.";
        Refresh();
    }

    private void EndDeleteMode()
    {
        _deleteMode = false;
        _deleteBaseline = null;
        _selectedInstance = default;
        _deleteChrome.Visible = false;
        RestorePlacementUi();
        _panel.Visible = true;
    }

    private async void Save()
    {
        if (_session is null || _saving) return;
        CancelUnusedReservation();
        _saving = true;
        try
        {
            await _saves.CommitEnvironmentAsync(_session);
            _economy.NotifyBalanceChanged();
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = $"Save failed: {exception.Message}";
            _confirm.Visible = false;
        }
        finally { _saving = false; }
    }

    private void RequestClose()
    {
        if (_saving || _session is null) return;
        if (_session.IsDirty)
        {
            _confirm.Visible = true;
            _confirm.MoveToFront();
            return;
        }
        Close();
    }

    private void Discard()
    {
        _session?.Cancel();
        Close();
    }

    private void Close()
    {
        _pinnable.Dock();
        _placement.Cancel();
        if (_placementMode) EndPlacementMode();
        if (_moveMode) CancelMoveMode();
        if (_deleteMode) CancelDeleteMode();
        _visuals.Preview(_state.Layout);
        _blocker.Visible = false;
        _confirm.Visible = false;
        _pointer.SetProcessInput(_pointerInputBefore);
        _pointer.SetProcessUnhandledInput(_pointerUnhandledBefore);
        _session = null;
        _moveMode = false;
        _deleteMode = false;
    }

    private void Refresh()
    {
        bool ownedCopyReady = _selectedDefinition is not null &&
            _session?.OwnedUnplacedCount(_selectedDefinition.ToDefinition().Id) > 0;
        long cost = _selectedDefinition?.ToDefinition().PriceMilliCredits ?? 0;
        long current = _progress.BalanceMilliCredits;
        long available = current;
        if (_session is not null) _session.TryProjectBalance(current, out available);
        long additionalCost = _selectedDefinition is null ? 0 : cost;
        long afterPurchase = available >= additionalCost ? available - additionalCost : 0;
        DecorationDefinitionId selectionId = SelectedDefinitionId();
        Color moneyColor = available >= additionalCost ? Color.Color8(0, 112, 0) : Color.Color8(176, 0, 0);

        if (_selectedDefinition is null && _selectedInstance != default)
        {
            _values.SetRows([
                new Win98ValueRowPresentation("available", "Available Funds", ContentDisplayName.Credits(available), true, true, moneyColor),
                new Win98ValueRowPresentation("selected", "Selected Item", selectionId == default ? "Unknown" : DisplayName(selectionId)),
                new Win98ValueRowPresentation("owned", "Owned", Owned(selectionId).ToString(), true),
            ]);
        }
        else
        {
            _values.SetRows([
                new Win98ValueRowPresentation("available", "Available Funds", ContentDisplayName.Credits(available), true, true, moneyColor),
                new Win98ValueRowPresentation("cost", "Item Cost", ContentDisplayName.Credits(cost)),
                new Win98ValueRowPresentation("projected", "After Purchase", ContentDisplayName.Credits(afterPurchase), true,
                    true, available >= additionalCost ? Color.Color8(0, 112, 0) : Color.Color8(176, 0, 0)),
                new Win98ValueRowPresentation("owned", "Owned", Owned(selectionId).ToString()),
            ]);
        }

        _buy.Disabled = _selectedDefinition is null || available < cost;
        _place.Disabled = _selectedDefinition is null || !ownedCopyReady;
        bool hasFurniture = _session?.WorkingLayout.Decorations.Any(item => item.RenderBand != DecorationRenderBand.Wallpaper) == true;
        bool hasAnything = _session?.WorkingLayout.Decorations.Count > 0;
        _move.Visible = hasFurniture;
        _move.Disabled = !hasFurniture;
        _delete.Visible = hasAnything;
        _delete.Disabled = !hasAnything;
        _rotateLeft.Disabled = !CanRotateSelected();
        _rotateRight.Disabled = !CanRotateSelected();
        RefreshSelectionSummary();
        RefreshCatalogueBadges();
    }

    private void RefreshSelectionSummary()
    {
        EnvironmentDecorationResource? resource = _selectedDefinition;
        if (resource is null && TryFindPlaced(_selectedInstance, out PlacedDecoration placed))
            resource = EnvironmentDecorationRegistry.Find(placed.DefinitionId);
        if (resource is null)
        {
            _selectionPreview.Texture = null;
            _selectionLabel.Text = "Select a catalogue item or a placed decoration.";
            return;
        }

        DecorationDefinition definition = resource.ToDefinition();
        _selectionPreview.Texture = Preview(resource);
        _selectionLabel.Text = $"{DisplayName(definition.Id)}  |  {ContentDisplayName.Credits(definition.PriceMilliCredits)}  |  Free placement";
    }

    private bool CanRotateSelected()
    {
        if (_selectedInstance == default || !TryFindPlaced(_selectedInstance, out PlacedDecoration placed)) return false;
        return EnvironmentDecorationRegistry.Find(placed.DefinitionId)?.ToDefinition().Rotation.AllowsRotation == true;
    }

    private Rect2 RoomRect() => EnvironmentRoomRect.Resolve(this);

    private static RoomScreenBounds ToBounds(Rect2 room) => new(room.Position.X, room.Position.Y, room.Size.X, room.Size.Y);

    private void CancelUnusedReservation()
    {
        if (_session?.HasReservation == true) _session.CancelReservation();
    }

    private void HideOtherUiForFocus()
    {
        _placementHiddenUi.Clear();
        HideTopControls(GetTree().Root);
    }

    private void HideTopControls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child == _blocker || _blocker.IsAncestorOf(child) || child is Win98BuddyShellController) continue;
            if (child is SubViewport) continue;
            if (child is Control control && control.Visible)
            {
                control.Visible = false;
                _placementHiddenUi.Add(control);
                continue;
            }
            HideTopControls(child);
        }
    }

    private void RestorePlacementUi()
    {
        foreach (Control control in _placementHiddenUi)
            if (GodotObject.IsInstanceValid(control)) control.Visible = true;
        _placementHiddenUi.Clear();
    }

    private PanelContainer FocusChrome(string name, string title, string text, out VBoxContainer body, out HBoxContainer actions)
    {
        PanelContainer chrome = Win98Dialog.Create(name, title, new Vector2(280, 96), out body);
        _blocker.AddChild(chrome);
        chrome.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        chrome.OffsetLeft = -400;
        chrome.OffsetTop = 12;
        chrome.OffsetRight = -12;
        chrome.OffsetBottom = 176;
        body.AddChild(new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });
        actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        return chrome;
    }

    private DecorationDefinitionId SelectedDefinitionId()
    {
        if (_selectedDefinition is not null) return _selectedDefinition.ToDefinition().Id;
        return TryFindPlaced(_selectedInstance, out PlacedDecoration placed) ? placed.DefinitionId : default;
    }

    private int Owned(DecorationDefinitionId id) => id == default || _session is null
        ? 0
        : _session.WorkingLayout.Decorations.Count(item => item.DefinitionId == id) + _session.OwnedUnplacedCount(id);

    private Win98CatalogItemPresentation CatalogPresentation(EnvironmentDecorationResource resource)
    {
        DecorationDefinition definition = resource.ToDefinition();
        return new Win98CatalogItemPresentation(definition.Id.Value, DisplayName(definition.Id),
            ContentDisplayName.Credits(definition.PriceMilliCredits), Preview(resource), true,
            "Free placement inside the room", $"Owned: {Owned(definition.Id)}");
    }

    private void RefreshCatalogueBadges()
    {
        foreach (EnvironmentDecorationResource resource in EnvironmentDecorationRegistry.Authored.Entries)
            if (resource.ToDefinition().Category == _selectedCategory) _catalogue.UpdateItem(CatalogPresentation(resource));
    }

    private bool TryFindPlaced(PlacedDecorationId id, out PlacedDecoration placed)
    {
        if (_session is not null)
        {
            foreach (PlacedDecoration item in _session.WorkingLayout.Decorations)
            {
                if (item.InstanceId != id) continue;
                placed = item;
                return true;
            }
        }
        placed = default;
        return false;
    }

    private static DecorationCategory[] VisibleCategories() => EnvironmentDecorationRegistry.Authored.Entries
        .Where(resource => resource.ToDefinition().Visible)
        .Select(resource => resource.ToDefinition().Category)
        .Distinct()
        .OrderBy(category => (int)category)
        .ToArray();

    private static Button Action(Control parent, string text, Action action)
    {
        var button = new Button { Name = $"Environment{text.Replace(" ", string.Empty)}Button", Text = text };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static string CategoryLabel(DecorationCategory category) => category == DecorationCategory.Sofa ? "Sofas" : category + "s";
    private static string DisplayName(DecorationDefinitionId id) => id.Value.Split('.').Last().Replace('_', ' ').ToTitleCase();
    private static Texture2D Preview(EnvironmentDecorationResource resource) =>
        EnvironmentDecorationVisualFactory.CreatePreview(resource);
}

internal static class EnvironmentTextExtensions
{
    public static string ToTitleCase(this string value) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
}
