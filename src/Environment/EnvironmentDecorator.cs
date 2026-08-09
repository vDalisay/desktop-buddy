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
    private Win98CategoryStrip _categories = null!;
    private Win98CatalogGrid _catalogue = null!;
    private Win98ValuePanel _values = null!;
    private Button _buy = null!;
    private Button _place = null!;
    private Button _move = null!;
    private Button _rotate = null!;
    private Button _sell = null!;
    private CheckBox _snap = null!;
    private OptionButton _grid = null!;
    private Label _status = null!;
    private PanelContainer _confirm = null!;
    private PanelContainer _placementChrome = null!;
    private Label _placementStatus = null!;
    private Button _placementDone = null!;
    private readonly List<Control> _placementHiddenUi = [];
    private EnvironmentDecorationResource? _selectedDefinition;
    private PlacedDecorationId _selectedInstance;
    private bool _moving;
    private CanonicalRoomPosition _moveOriginal;
    private bool _saving;
    private bool _pointerInputBefore;
    private bool _pointerUnhandledBefore;
    private bool _placementMode;
    private PlacedDecorationId _placementStagedInstance;
    private bool _panelPositioned;
    private Vector2 _lastViewportSize;
    private RoomScreenBounds _placementBounds;

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

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        LabPointerGrabComponent pointer,
        EnvironmentProgressState state,
        SaveCoordinator saves,
        EnvironmentDecorationLayer visuals)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
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
        float height = Mathf.Clamp(viewport.Y * .52f, 260, 400);
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
        if (_placementMode) { CancelPlacement(); GetViewport().SetInputAsHandled(); return; }
        if (_placement.Active || _moving || _selectedInstance != default) CancelTransient(); else RequestClose();
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        if (IsOpen || _saving) return;
        _session = new EnvironmentEditSession(_state.Layout, _progress.BalanceMilliCredits, EnvironmentDecorationRegistry.Domain);
        _placement.Configure(_session, _blocker, ToBounds(RoomRect()));
        _pointerInputBefore = _pointer.IsProcessingInput();
        _pointerUnhandledBefore = _pointer.IsProcessingUnhandledInput();
        _pointer.SetProcessInput(false);
        _pointer.SetProcessUnhandledInput(false);
        _blocker.Visible = true;
        _panel.Visible = true;
        _confirm.Visible = false;
        _selectedDefinition = null;
        _selectedInstance = default;
        _moving = false;
        _placementMode = false;
        _placementStagedInstance = default;
        SelectCategory(DecorationCategory.Lamp.ToString());
        _visuals.Preview(_session.WorkingLayout);
        _status.Text = "Select an item, then choose Buy.";
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
                _placementStatus.Text = "Position staged. Choose Done to keep it or Cancel to remove it.";
                _placementDone.Disabled = false;
            }
            else _placementStatus.Text = result.Status.ToString();
            Refresh();
        };

        _panel = Win98Dialog.Create("EnvironmentDecoratorPanel", "Environment Decorator", new Vector2(620, 370), out VBoxContainer body, RequestClose);
        _panel.CustomMinimumSize = new Vector2(360, 260);
        _blocker.AddChild(_panel);
        _panel.Visible = true;

        _categories = new Win98CategoryStrip { Name = "EnvironmentCategories" };
        _categories.SetItems(Enum.GetValues<DecorationCategory>().Select(category =>
            new Win98CategoryPresentation(category.ToString(), CategoryLabel(category), Tooltip: $"Browse {CategoryLabel(category).ToLowerInvariant()}.")));
        _categories.SelectionChanged += SelectCategory;
        body.AddChild(_categories);

        _catalogue = new Win98CatalogGrid { Name = "EnvironmentCatalog", CustomMinimumSize = new Vector2(0, 150) };
        _catalogue.ConfigureTileSize(116, 132);
        _catalogue.SelectionChanged += SelectDefinition;
        body.AddChild(_catalogue);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 6);
        body.AddChild(controls);
        _snap = new CheckBox { Text = "Snap to grid" };
        _snap.Toggled += enabled => { _placement.SnapEnabled = enabled; _grid.Disabled = !enabled; };
        controls.AddChild(_snap);
        _grid = new OptionButton { Disabled = true, CustomMinimumSize = new Vector2(90, 28) };
        foreach (EnvironmentGridSize size in Enum.GetValues<EnvironmentGridSize>()) _grid.AddItem(size.ToString(), (int)size);
        _grid.ItemSelected += index => _placement.GridSize = (EnvironmentGridSize)_grid.GetItemId((int)index);
        controls.AddChild(_grid);
        _buy = Action(controls, "Buy", BuySelected);
        _place = Action(controls, "Place", BeginPlacement);
        _move = Action(controls, "Move", BeginMove);
        _rotate = Action(controls, "Rotate", RotateSelected);
        _sell = Action(controls, "Sell", SellSelected);

        _values = new Win98ValuePanel { Name = "EnvironmentBudget" };
        body.AddChild(_values);
        _status = new Label { Name = "EnvironmentDecoratorStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(_status);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Done", Save).Name = "EnvironmentDoneButton";
        Win98Dialog.Action(actions, "Cancel", RequestClose).Name = "EnvironmentCancelButton";

        _confirm = Win98Dialog.Create("EnvironmentDecoratorUnsaved", "Unsaved Room", new Vector2(360, 170), out VBoxContainer confirmBody);
        _blocker.AddChild(_confirm);
        confirmBody.AddChild(new Label { Text = "Save room changes before closing?" });
        var confirmActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        confirmBody.AddChild(confirmActions);
        Win98Dialog.Action(confirmActions, "Save", Save).Name = "EnvironmentConfirmSaveButton";
        Win98Dialog.Action(confirmActions, "Discard", Discard).Name = "EnvironmentDiscardButton";
        Win98Dialog.Action(confirmActions, "Keep Editing", () => _confirm.Visible = false).Name = "EnvironmentKeepEditingButton";

        _placementChrome = new PanelContainer
        {
            Name = "EnvironmentPlacementChrome",
            Visible = false,
            Theme = Win98ThemeFactory.Create(),
        };
        _placementChrome.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _placementChrome.OffsetLeft = -300;
        _placementChrome.OffsetTop = 12;
        _placementChrome.OffsetRight = -12;
        _placementChrome.OffsetBottom = 112;
        _blocker.AddChild(_placementChrome);
        var placementBody = new VBoxContainer();
        _placementChrome.AddChild(placementBody);
        _placementStatus = new Label { Text = "Click a valid room position.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        placementBody.AddChild(_placementStatus);
        var placementActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        placementBody.AddChild(placementActions);
        _placementDone = Win98Dialog.Action(placementActions, "Done", ConfirmPlacement);
        _placementDone.Name = "EnvironmentPlacementDoneButton";
        Win98Dialog.Action(placementActions, "Cancel", CancelPlacement).Name = "EnvironmentPlacementCancelButton";
    }

    private void SelectCategory(string id)
    {
        if (!Enum.TryParse(id, out DecorationCategory category)) return;
        CancelUnusedReservation();
        _placement.Cancel();
        _selectedDefinition = null;
        _selectedInstance = default;
        var items = new List<Win98CatalogItemPresentation>();
        foreach (EnvironmentDecorationResource resource in EnvironmentDecorationRegistry.Authored.Entries)
        {
            DecorationDefinition definition = resource.ToDefinition();
            if (!definition.Visible || definition.Category != category) continue;
            items.Add(new Win98CatalogItemPresentation(definition.Id.Value, DisplayName(definition.Id),
                ContentDisplayName.Credits(definition.PriceMilliCredits), Preview(resource), true,
                $"{definition.AnchorKind} decoration"));
        }
        _catalogue.SetItems(items);
        _status.Text = "Select an item, then choose Buy.";
        Refresh();
    }

    private void SelectDefinition(string id)
    {
        if (!DecorationDefinitionId.TryCreate(id, out DecorationDefinitionId definitionId)) return;
        if (_session?.HasReservation == true && _session.ReservedDefinitionId != definitionId) _session.CancelReservation();
        _selectedDefinition = EnvironmentDecorationRegistry.Find(definitionId);
        _selectedInstance = default;
        _placement.Cancel();
        _status.Text = _selectedDefinition is null ? "Item unavailable." : "Choose Buy to reserve one copy, then Place.";
        Refresh();
    }

    private void BuySelected()
    {
        if (_selectedDefinition is null || _session is null) return;
        EnvironmentEditResult result = _session.Reserve(_selectedDefinition.ToDefinition().Id, _progress.BalanceMilliCredits);
        _status.Text = result.Succeeded ? "One copy reserved. Choose Place to position it." :
            result.Status == EnvironmentEditStatus.InsufficientFunds ? "Not enough current funds to buy this copy." : result.Status.ToString();
        Refresh();
    }

    private void BeginPlacement()
    {
        if (_selectedDefinition is null || _session is null || !_session.HasReservation ||
            _session.ReservedDefinitionId != _selectedDefinition.ToDefinition().Id) return;
        _selectedInstance = default;
        _placementMode = true;
        _placementStagedInstance = default;
        _panel.Visible = false;
        _placementBounds = ToBounds(RoomRect());
        HideOtherUiForPlacement();
        _placementChrome.Visible = true;
        _placementDone.Disabled = true;
        _placementStatus.Text = "Click a valid room position.";
        _placement.Begin(_selectedDefinition);
        _placement.UpdateRoom(_placementBounds);
        Refresh();
    }

    private void BeginMove()
    {
        if (_session is null || _selectedInstance == default) return;
        if (!TryFindPlaced(_selectedInstance, out PlacedDecoration selected)) return;
        _moveOriginal = selected.Position;
        _moving = true;
        _status.Text = "Move the pointer; left-click confirms, Escape restores.";
    }

    private void RotateSelected()
    {
        if (_session is null || _selectedInstance == default) return;
        EnvironmentEditResult result = _session.Rotate(_selectedInstance);
        _status.Text = result.Succeeded ? "Rotated." : result.Status.ToString();
        _visuals.Preview(_session.WorkingLayout);
        Refresh();
    }

    private void SellSelected()
    {
        if (_session is null || _selectedInstance == default) return;
        EnvironmentEditResult result = _session.Sell(_selectedInstance);
        if (result.Succeeded) { _selectedInstance = default; _visuals.Preview(_session.WorkingLayout); }
        _status.Text = result.Succeeded ? "Removed; refund is staged until Done." : result.Status.ToString();
        Refresh();
    }

    private void OnRoomInput(InputEvent input)
    {
        if (_session is null || _confirm.Visible) { _blocker.AcceptEvent(); return; }
        if (_placementMode && !_placement.Active) { _blocker.AcceptEvent(); return; }
        switch (input)
        {
            case InputEventMouseMotion motion when _placement.Active:
                _placement.UpdatePointer(motion.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when _placement.Active:
                _placement.UpdatePointer(click.Position);
                _placement.CommitGhost();
                break;
            case InputEventMouseMotion motion when _moving:
                MoveSelected(motion.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when _moving:
                _moving = false;
                _status.Text = "Move confirmed.";
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                SelectPlaced(click.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                CancelTransient();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                if (_placement.Active || _moving || _selectedInstance != default) CancelTransient(); else RequestClose();
                break;
        }
        _blocker.AcceptEvent();
    }

    private void MoveSelected(Vector2 screen)
    {
        if (_session is null || _selectedInstance == default) return;
        if (!TryFindPlaced(_selectedInstance, out PlacedDecoration selected)) return;
        EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Find(selected.DefinitionId);
        if (resource is null) return;
        if (EnvironmentPlacement.TryMap(screen.X, screen.Y, ToBounds(RoomRect()), resource.ToDefinition().AnchorKind,
            _snap.ButtonPressed, _placement.GridSize, out CanonicalRoomPosition canonical))
        {
            _session.Move(_selectedInstance, canonical);
            _visuals.Preview(_session.WorkingLayout);
        }
    }

    private void SelectPlaced(Vector2 screen)
    {
        if (_session is null || !EnvironmentPlacement.TryMap(screen.X, screen.Y, ToBounds(RoomRect()),
            DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition point)) return;
        if (_visuals.TryHit(point, out PlacedDecorationId id))
        {
            _selectedInstance = id;
            _selectedDefinition = null;
            _status.Text = "Placed item selected.";
        }
        else _selectedInstance = default;
        Refresh();
    }

    private void CancelTransient()
    {
        if (_moving && _session is not null) _session.Move(_selectedInstance, _moveOriginal);
        _moving = false;
        _placement.Cancel();
        _selectedInstance = default;
        if (_session is not null) _visuals.Preview(_session.WorkingLayout);
        Refresh();
    }

    private void ConfirmPlacement()
    {
        if (!_placementMode || _placementStagedInstance == default) return;
        PlacedDecorationId placed = _placementStagedInstance;
        EndPlacementMode();
        _selectedDefinition = null;
        _selectedInstance = placed;
        _status.Text = "Placed item selected. Move, Rotate, or Sell are available below.";
        Refresh();
    }

    private void CancelPlacement()
    {
        if (!_placementMode || _session is null) return;
        if (_placementStagedInstance != default) _session.Sell(_placementStagedInstance);
        else if (_session.HasReservation) _session.CancelReservation();
        EndPlacementMode();
        _status.Text = "Placement cancelled; the reserved cost was restored.";
        _visuals.Preview(_session.WorkingLayout);
        Refresh();
    }

    private void EndPlacementMode()
    {
        _placement.Cancel();
        _placementMode = false;
        _placementStagedInstance = default;
        _placementChrome.Visible = false;
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
        catch (Exception exception) { _status.Text = $"Save failed: {exception.Message}"; _confirm.Visible = false; }
        finally { _saving = false; }
    }

    private void RequestClose()
    {
        if (_saving || _session is null) return;
        if (_session.IsDirty) { _confirm.Visible = true; return; }
        Close();
    }

    private void Discard() { _session?.Cancel(); Close(); }

    private void Close()
    {
        _placement.Cancel();
        if (_placementMode) EndPlacementMode();
        _visuals.Preview(_state.Layout);
        _blocker.Visible = false;
        _confirm.Visible = false;
        _pointer.SetProcessInput(_pointerInputBefore);
        _pointer.SetProcessUnhandledInput(_pointerUnhandledBefore);
        _session = null;
        _moving = false;
    }

    private void Refresh()
    {
        long cost = _selectedDefinition?.ToDefinition().PriceMilliCredits ?? 0;
        long current = _progress.BalanceMilliCredits;
        long projected = current;
        if (_session is not null) _session.TryProjectBalance(current, out projected);
        _values.SetRows([
            new("available", "Available Funds", ContentDisplayName.Credits(current)),
            new("cost", "Item Cost", ContentDisplayName.Credits(cost)),
            new("projected", "Projected Funds", ContentDisplayName.Credits(projected), true),
        ]);
        bool matchingReservation = _session?.HasReservation == true && _selectedDefinition is not null &&
            _session.ReservedDefinitionId == _selectedDefinition.ToDefinition().Id;
        _buy.Disabled = _selectedDefinition is null || _session?.HasReservation == true || projected < cost;
        _place.Disabled = !matchingReservation;
        _move.Visible = _selectedInstance != default;
        _rotate.Visible = _selectedInstance != default;
        _sell.Visible = _selectedInstance != default;
    }

    private Rect2 RoomRect()
    {
        var frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        Rect2 room = GodotObject.IsInstanceValid(frame) ? frame!.ContentViewportRect : GetViewport().GetVisibleRect();
        if (GetTree().Root.FindChild("Win98CommandBar", true, false) is Control bar && bar.Visible)
        {
            float top = Math.Max(room.Position.Y, bar.GetGlobalRect().End.Y);
            room = new Rect2(room.Position.X, top, room.Size.X, Math.Max(1, room.End.Y - top));
        }
        return room;
    }

    private static RoomScreenBounds ToBounds(Rect2 room) => new(room.Position.X, room.Position.Y, room.Size.X, room.Size.Y);
    private void CancelUnusedReservation()
    {
        if (_session?.HasReservation == true) _session.CancelReservation();
    }

    private void HideOtherUiForPlacement()
    {
        _placementHiddenUi.Clear();
        HideTopControls(GetTree().Root);
    }

    private void HideTopControls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child == _blocker || _blocker.IsAncestorOf(child)) continue;
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
    private static Button Action(Control parent, string text, Action action) { var button = new Button { Name = $"Environment{text}Button", Text = text }; button.Pressed += action; parent.AddChild(button); return button; }
    private static string CategoryLabel(DecorationCategory category) => category == DecorationCategory.Sofa ? "Sofas" : category + "s";
    private static string DisplayName(DecorationDefinitionId id) => id.Value.Split('.').Last().Replace('_', ' ').ToTitleCase();

    private static Texture2D Preview(EnvironmentDecorationResource resource)
    {
        Image image = Image.CreateEmpty(48, 48, false, Image.Format.Rgba8);
        image.Fill(resource.SecondaryColor);
        for (int y = 5; y < 43; y++) for (int x = 5; x < 43; x++) image.SetPixel(x, y, resource.PrimaryColor);
        return ImageTexture.CreateFromImage(image);
    }
}

internal static class EnvironmentTextExtensions
{
    public static string ToTitleCase(this string value) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
}
