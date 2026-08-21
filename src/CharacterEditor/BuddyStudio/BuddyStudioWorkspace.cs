using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Economy;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>Buddy-specific controller for the shared Win98 customization controls.</summary>
public partial class BuddyStudioWorkspace : VBoxContainer
{
    private const float MinimumViewZoom = 0.75f;
    private const float MaximumViewZoom = 2.0f;
    private const float ViewZoomStep = 0.2f;
    private static readonly CharacterFeatureSlot[] AllCategories =
    [
        CharacterFeatureSlot.Face, CharacterFeatureSlot.Hair, CharacterFeatureSlot.Brows,
        CharacterFeatureSlot.Eyes, CharacterFeatureSlot.Nose, CharacterFeatureSlot.Mouth,
        CharacterFeatureSlot.Ears, CharacterFeatureSlot.Glasses, CharacterFeatureSlot.Headwear,
        CharacterFeatureSlot.Tops, CharacterFeatureSlot.Shoes,
    ];

    /// <summary>The categories this build ships; the Demo holds Tops and Shoes back. Read
    /// rather than cached so a scenario can widen the scope and see the difference.</summary>
    private static CharacterFeatureSlot[] CategoryOrder =>
        AllCategories.Where(slot => DemoScope.Includes(slot)).ToArray();

    private static readonly (string Name, Rgba32 Color)[] Palette =
    [
        ("Ink", Rgba32.Parse("#183042")), ("Cocoa", Rgba32.Parse("#6A4937")),
        ("Berry", Rgba32.Parse("#C95B63")), ("Gold", Rgba32.Parse("#E3A33A")),
        ("Sky", Rgba32.Parse("#74B9E8")), ("Slate", Rgba32.Parse("#5A6575")),
    ];

    private CharacterEditorSession _session = null!;
    private EconomyService _economy = null!;
    private Action _closeImmediately = null!;
    private Func<Task> _flushProgress = static () => Task.CompletedTask;
    private Win98CategoryStrip _categories = null!;
    private Win98CatalogGrid _catalog = null!;
    private Win98ValuePanel _values = null!;
    private ColorPickerButton _color = null!;
    private Control _presets = null!;
    private Button _buy = null!;
    private Button _save = null!;
    private Button _move = null!;
    private Button _smaller = null!;
    private Button _larger = null!;
    private Button _resetTransform = null!;
    private Label _name = null!;
    private Label _status = null!;
    private Control _previewInput = null!;
    private Camera3D _previewCamera = null!;
    private VBoxContainer _previewColumn = null!;
    private Node? _previewHome;
    private int _previewHomeIndex;
    private Win98WindowFrame? _frame;
    private BuddyVisualRigView? _previewRig;
    private Control? _paintCanvas;
    private bool _paintCanvasWasVisible;
    private bool _previewAttached;
    private bool _cameraStateCaptured;
    private Vector3 _cameraHomePosition;
    private float _cameraHomeSize;
    private Control _dirtyBlocker = null!;
    private PanelContainer _dirtyDialog = null!;
    private Control _moveBlocker = null!;
    private CharacterFeatureSlot _slot = CharacterFeatureSlot.Face;
    private bool _refreshing;
    private bool _moveMode;
    private bool _dragging;
    private Control? _movePreviousFocus;
    private int _previewHomeZIndex;
    private CursorShape _previewHomeCursor;
    private float _viewZoom = 1.0f;

    public bool IsConfigured { get; private set; }
    public bool MoveMode => _moveMode;
    public CharacterFeatureSlot SelectedSlot => _slot;
    public Button SaveAction => _save;
    public Button BuyAction => _buy;
    public Win98CategoryStrip CategoryStrip => _categories;
    public Win98CatalogGrid CatalogGrid => _catalog;
    public float ViewZoom => _viewZoom;
    public float PreviewCameraSize => _previewCamera.Size;
    public Vector2 PreviewFocus => new(_previewCamera.Position.X, _previewCamera.Position.Y);

    public void AttachPreview()
    {
        if (_previewAttached || !GodotObject.IsInstanceValid(_previewColumn))
            return;
        _cameraHomePosition = _previewCamera.Position;
        _cameraHomeSize = _previewCamera.Size;
        _cameraStateCaptured = true;
        _previewHome = _previewInput.GetParent();
        _previewHomeIndex = _previewInput.GetIndex();
        _paintCanvas = _previewInput.FindChild("CharacterPaintCanvas", true, false) as Control;
        if (GodotObject.IsInstanceValid(_paintCanvas))
        {
            _paintCanvasWasVisible = _paintCanvas!.Visible;
            _paintCanvas.Visible = false;
        }
        _previewInput.Reparent(_previewColumn);
        _previewAttached = true;
        if (!GodotObject.IsInstanceValid(_previewRig))
            _previewRig = _previewInput.FindChildren("*", nameof(BuddyVisualRigView), true, false)
                .OfType<BuddyVisualRigView>()
                .FirstOrDefault();
        _previewRig?.SetPreviewFaceState(BuiltInCharacterAppearance.NeutralFaceState);
        ResetView();
    }

    public void DetachPreview()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewHome))
            return;
        _previewInput.Reparent(_previewHome!);
        _previewHome!.MoveChild(_previewInput, Math.Min(_previewHomeIndex, _previewHome.GetChildCount() - 1));
        if (GodotObject.IsInstanceValid(_paintCanvas))
            _paintCanvas!.Visible = _paintCanvasWasVisible;
        if (_cameraStateCaptured && GodotObject.IsInstanceValid(_previewCamera))
        {
            _previewCamera.Position = _cameraHomePosition;
            _previewCamera.Size = _cameraHomeSize;
        }
        _cameraStateCaptured = false;
        _previewRig?.ClearPreviewFaceState();
        SetMoveMode(false);
        _previewAttached = false;
        if (GodotObject.IsInstanceValid(_frame))
            _frame!.StatusText = "Ready";
    }

    public void Configure(
        CharacterEditorSession session,
        EconomyService economy,
        Control preview,
        Camera3D previewCamera,
        Label status,
        Action closeImmediately,
        Func<Task>? flushProgress = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Buddy Studio must be configured before entering the tree.");
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _previewInput = preview ?? throw new ArgumentNullException(nameof(preview));
        _previewCamera = previewCamera ?? throw new ArgumentNullException(nameof(previewCamera));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _closeImmediately = closeImmediately ?? throw new ArgumentNullException(nameof(closeImmediately));
        _flushProgress = flushProgress ?? (static () => Task.CompletedTask);
        IsConfigured = true;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Buddy Studio was not configured.");
        Name = "BuddyStudioWorkspace";
        Theme = Win98ThemeFactory.Create();
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        BuildUi();
        _session.Changed += Refresh;
        _economy.BalanceChanged += OnBalanceChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        DetachPreview();
        if (IsConfigured)
        {
            _session.Changed -= Refresh;
            _economy.BalanceChanged -= OnBalanceChanged;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.CtrlPressed && key.Keycode == Key.S)
        {
            _ = SaveAsync();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_moveMode)
            return;
        if (key.Keycode == Key.Escape)
        {
            SetMoveMode(false);
            GetViewport().SetInputAsHandled();
            return;
        }
        Vector2 direction = key.Keycode switch
        {
            Key.Left => Vector2.Left,
            Key.Right => Vector2.Right,
            Key.Up => Vector2.Up,
            Key.Down => Vector2.Down,
            _ => Vector2.Zero,
        };
        if (direction != Vector2.Zero)
        {
            // Screen-space direction, flipped to the document's Y-up convention like the drag.
            Nudge(new Vector2(direction.X, -direction.Y) *
                (key.ShiftPressed ? 0.05f : 0.01f));
            GetViewport().SetInputAsHandled();
        }
    }

    public void SelectCategory(CharacterFeatureSlot slot)
    {
        if (!CategoryOrder.Contains(slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        _session.CancelCosmeticPreviews();
        _slot = slot;
        _categories.Select(SlotId(slot), notify: false);
        SetMoveMode(false);
        ResetView();
        RefreshCatalog();
        RefreshSelectionPane();
    }

    private void BuildUi()
    {
        _categories = new Win98CategoryStrip { Name = "BuddyStudioCategories" };
        _categories.SetItems(CategoryOrder.Select(slot => new Win98CategoryPresentation(
            SlotId(slot), Friendly(slot), null, true, $"Edit {Friendly(slot).ToLowerInvariant()}.")));
        _categories.SelectionChanged += id => SelectCategory(ParseSlot(id));
        AddChild(_categories);

        var bodyScroll = new ScrollContainer
        {
            Name = "BuddyStudioBodyScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(bodyScroll);
        var body = new HBoxContainer
        {
            Name = "BuddyStudioBody",
            CustomMinimumSize = new Vector2(900, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 8);
        bodyScroll.AddChild(body);
        BuildPreviewPane(body);
        BuildCatalogPane(body);
        BuildInspectorPane(body);

        BuildDirtyDialog();
        BuildMoveBlocker();
        _categories.Select(SlotId(_slot), notify: false);
    }

    private void BuildPreviewPane(HBoxContainer body)
    {
        var pane = Pane("BuddyStudioPreviewPane", 280);
        body.AddChild(pane);
        _previewColumn = Column(pane);
        _previewColumn.AddChild(new Label { Text = "Preview" });
        _name = new Label { Name = "BuddyStudioCharacterName", HorizontalAlignment = HorizontalAlignment.Center };
        _previewColumn.AddChild(_name);
        _previewInput.CustomMinimumSize = new Vector2(270, 300);
        _previewInput.SizeFlagsVertical = SizeFlags.ExpandFill;
        _previewInput.MouseFilter = MouseFilterEnum.Stop;

        var view = new HBoxContainer
        {
            Name = "BuddyStudioViewActions",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        view.AddThemeConstantOverride("separation", 4);
        _previewColumn.AddChild(view);
        Button zoomOut = ViewAction(view, "Zoom −", () => SetViewZoom(_viewZoom - ViewZoomStep));
        zoomOut.Name = "BuddyStudioZoomOut";
        zoomOut.TooltipText = "Show more of the buddy portrait.";
        Button zoomIn = ViewAction(view, "Zoom +", () => SetViewZoom(_viewZoom + ViewZoomStep));
        zoomIn.Name = "BuddyStudioZoomIn";
        zoomIn.TooltipText = "Move closer to the selected area.";
        Button resetView = ViewAction(view, "Reset View", ResetView);
        resetView.Name = "BuddyStudioResetView";
        resetView.TooltipText = "Restore the selected category's default portrait framing.";

        var transform = new GridContainer
        {
            Name = "BuddyStudioTransformActions",
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _previewColumn.AddChild(transform);
        _smaller = Action(transform, "Smaller", () => ScaleBy(-0.05));
        _smaller.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _smaller.Name = "BuddyStudioSmaller";
        _larger = Action(transform, "Larger", () => ScaleBy(0.05));
        _larger.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _larger.Name = "BuddyStudioLarger";
        _move = Action(transform, "Move", () => SetMoveMode(!_moveMode));
        _move.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _move.Name = "BuddyStudioMove";
        _resetTransform = Action(transform, "Reset", ResetTransform);
        _resetTransform.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _resetTransform.Name = "BuddyStudioReset";
        Button random = Action(_previewColumn, "Randomize", Randomize);
        random.Name = "BuddyStudioRandomize";
        random.TooltipText = "Choose from free and owned cosmetics only.";
    }

    private void BuildCatalogPane(HBoxContainer body)
    {
        var pane = Pane("BuddyStudioCatalogPane", 330);
        pane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(pane);
        var column = Column(pane);
        column.AddChild(new Label { Text = "Styles" });
        // Squares of artwork: the category strip already names the category, and the tile
        // name repeated it (owner instruction 2026-08-21). Price and badges stay.
        _catalog = new Win98CatalogGrid { Name = "BuddyStudioCatalog", ShowItemNames = false };
        _catalog.ConfigureTileSize(122, 142);
        _catalog.SelectionChanged += SelectCosmetic;
        _catalog.ItemActivated += cosmeticId => _ = ActivateCosmeticAsync(cosmeticId);
        column.AddChild(_catalog);
    }

    private void BuildInspectorPane(HBoxContainer body)
    {
        var pane = Pane("BuddyStudioInspectorPane", 250);
        body.AddChild(pane);
        var column = Column(pane);
        column.AddChild(new Label { Text = "Color and Ownership" });
        _color = new ColorPickerButton
        {
            Name = "BuddyStudioColor",
            Text = "Choose color",
            CustomMinimumSize = new Vector2(0, 34),
        };
        _color.ColorChanged += color =>
        {
            if (!_refreshing)
                Handle(_session.SetFeatureColor(_slot, ToRgba(color)));
        };
        column.AddChild(_color);
        _presets = new GridContainer
        {
            Name = "BuddyStudioColorPresets",
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddChild(_presets);
        foreach ((string name, Rgba32 color) in Palette)
        {
            Rgba32 captured = color;
            var preset = new Button
            {
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(68, 32),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = $"Use {name} ({color.ToHex()}).",
            };
            Color swatch = FromRgba(color);
            preset.AddThemeStyleboxOverride("normal", Win98ThemeFactory.Raised(swatch, 2));
            preset.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Raised(swatch.Lightened(0.18f), 2));
            preset.AddThemeStyleboxOverride("pressed", Win98ThemeFactory.Recessed(swatch, 2));
            preset.Pressed += () => Handle(_session.SetFeatureColor(_slot, captured));
            _presets.AddChild(preset);
        }
        _values = new Win98ValuePanel { Name = "BuddyStudioValues" };
        column.AddChild(_values);
        _buy = Action(column, "Buy", () => _ = PurchaseOrEquipAsync());
        _buy.Name = "BuddyStudioBuy";

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var actions = new HBoxContainer
        {
            Name = "BuddyStudioActions",
            Alignment = BoxContainer.AlignmentMode.End,
        };
        actions.AddThemeConstantOverride("separation", 6);
        column.AddChild(actions);
        _save = Action(actions, "Save", () => _ = SaveAsync());
        _save.Name = "BuddyStudioSave";
        _save.CustomMinimumSize = new Vector2(96, 30);
        _save.TooltipText = "Save this character (Ctrl+S).";
        Button exit = Action(actions, "Exit", () => _ = CancelAsync());
        exit.Name = "BuddyStudioCancel";
        exit.CustomMinimumSize = new Vector2(96, 30);
    }

    private void BuildDirtyDialog()
    {
        _dirtyBlocker = Win98Dialog.Blocker((Control)GetParent(), "BuddyStudioDirtyBlocker");
        _dirtyDialog = Win98Dialog.Create(
            "BuddyStudioDirtyDialog", "Unsaved changes", new Vector2(420, 190),
            out VBoxContainer body);
        body.AddChild(new Label
        {
            Text = "Save your Buddy Studio changes before closing?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        body.AddChild(new Control
        {
            Name = "BuddyStudioUnsavedSpacer",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        });
        var actions = new HBoxContainer
        {
            Name = "BuddyStudioUnsavedActions",
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        body.AddChild(actions);
        Button save = Win98Dialog.Action(actions, "Save", () => _ = ResolveCloseAsync(UnsavedDecision.Save));
        save.Name = "BuddyStudioUnsavedSave";
        Button discard = Win98Dialog.Action(actions, "Discard", () => _ = ResolveCloseAsync(UnsavedDecision.Discard));
        discard.Name = "BuddyStudioUnsavedDiscard";
        Button keepEditing = Win98Dialog.Action(actions, "Keep Editing", HideDirtyDialog);
        keepEditing.Name = "BuddyStudioUnsavedKeepEditing";
        _dirtyBlocker.AddChild(_dirtyDialog);
    }

    private void BuildMoveBlocker()
    {
        _moveBlocker = Win98Dialog.Blocker((Control)GetParent(), "BuddyStudioMoveBlocker");
        _moveBlocker.ZIndex = 100;
        _moveBlocker.FocusMode = FocusModeEnum.All;
        _moveBlocker.TooltipText = "Drag the portrait to move it; click outside it or press Escape to finish.";
        _moveBlocker.GuiInput += OnMoveBlockerInput;
    }

    /// <summary>
    /// Godot picks GUI input by tree order, never by z_index, so the full-rect blocker is always hit
    /// first even though the raised preview draws above its dim. The blocker therefore owns move-mode
    /// input and routes by pointer position: inside the portrait drags, outside ends the mode.
    /// </summary>
    private void OnMoveBlockerInput(InputEvent input)
    {
        if (input is not InputEventMouse mouse || !GodotObject.IsInstanceValid(_previewInput))
            return;
        bool overPreview = _previewInput.GetGlobalRect().HasPoint(mouse.GlobalPosition);
        _moveBlocker.MouseDefaultCursorShape = overPreview ? CursorShape.Move : CursorShape.Arrow;
        if (mouse is InputEventMouseButton button)
        {
            if (!overPreview)
            {
                if (button.Pressed)
                    SetMoveMode(false);
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                _dragging = button.Pressed;
            }
            _moveBlocker.AcceptEvent();
            return;
        }
        if (!_dragging || mouse is not InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion)
            return;
        Nudge(ToDocumentOffset(motion.Relative));
        _moveBlocker.AcceptEvent();
    }

    /// <summary>
    /// Screen pixels to document offset units, so a dragged feature stays pinned under the cursor.
    /// The orthographic preview camera spreads its <see cref="Camera3D.Size"/> across the viewport
    /// height, and one document offset unit travels <see cref="CharacterFeatureTransform.OffsetExtent"/>
    /// of the half-extent of the surface the feature rides.
    /// </summary>
    private Vector2 ToDocumentOffset(Vector2 pixels)
    {
        float pixelsPerWorldUnit = _previewInput.Size.Y / _previewCamera.Size;
        float pixelsPerOffsetUnit =
            pixelsPerWorldUnit * CharacterFeatureTransform.OffsetExtent * FeatureHalfExtent(_slot);
        if (!float.IsFinite(pixelsPerOffsetUnit) || pixelsPerOffsetUnit <= 0f)
            return Vector2.Zero;
        // Screen Y grows downward and documented offsetY grows upward, on every surface alike.
        return new Vector2(pixels.X, -pixels.Y) / pixelsPerOffsetUnit;
    }

    /// <summary>
    /// The half-extent of the surface a feature rides: composited decals span their plate, while
    /// anchored 3D cosmetics are placed against the head radius.
    /// </summary>
    private float FeatureHalfExtent(CharacterFeatureSlot slot) => slot switch
    {
        CharacterFeatureSlot.Eyes or CharacterFeatureSlot.Brows or CharacterFeatureSlot.Mouth =>
            ParametricFaceCompositor.PlateWorldSize * 0.5f,
        CharacterFeatureSlot.Accessories =>
            BuddyVisualRigView.AccentPlateWorldSize * 0.5f,
        _ => HeadRadius(),
    };

    private float HeadRadius()
    {
        if (!GodotObject.IsInstanceValid(_previewRig))
            _previewRig = _previewInput.FindChildren("*", nameof(BuddyVisualRigView), true, false)
                .OfType<BuddyVisualRigView>()
                .FirstOrDefault();
        return GodotObject.IsInstanceValid(_previewRig)
            ? _previewRig!.PartMeshRadius(BuddyPartId.Head)
            : ParametricFaceCompositor.PlateWorldSize * 0.5f;
    }

    private void Refresh()
    {
        if (!IsInsideTree())
            return;
        _refreshing = true;
        try
        {
            CharacterDocument? document = _session.PreviewDocument;
            _name.Text = document?.DisplayName ?? "No character selected";
            _save.Disabled = !_session.IsDirty;
            _save.TooltipText = "Save this character (Ctrl+S).";
            RefreshCatalog();
            RefreshSelectionPane();
            SetStatus(_session.LastError ?? StatusText(document));
            if (_session.PendingAction == CharacterEditorPendingAction.Close && _dirtyBlocker.Visible &&
                GetTree().Root.FindChild("UnsavedChangesPrompt", true, false) is Control legacyPrompt)
                legacyPrompt.Visible = false;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshCatalog()
    {
        IReadOnlyList<CosmeticDefinition> definitions = CharacterFeatureCatalog.Shipped.GetDefinitions(_slot);
        _catalog.SetItems(definitions.Select(Presentation));
        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is not null)
            _catalog.Select(CharacterDocumentEditor.ReadFeatureId(preview, _slot), notify: false);
    }

    private Win98CatalogItemPresentation Presentation(CosmeticDefinition definition)
    {
        bool owned = _session.IsCosmeticOwned(definition.Id);
        bool equipped = IsEquipped(definition.Id);
        bool previewed = _session.PreviewDocument is CharacterDocument preview &&
            string.Equals(CharacterDocumentEditor.ReadFeatureId(preview, _slot), definition.Id, StringComparison.Ordinal);
        string secondary = owned ? string.Empty : PriceText(definition);
        Color? priceColor = null;
        if (!owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out CatalogueEntry entry) && entry.HasValidPrice)
        {
            priceColor = entry.PriceMilliCredits <= _economy.BalanceMilliCredits
                ? Color.Color8(0, 128, 0)
                : Color.Color8(192, 0, 0);
        }
        return new Win98CatalogItemPresentation(
            definition.Id,
            CosmeticName(definition),
            secondary,
            BuddyStudioThumbnailCache.For(definition),
            Tooltip: equipped ? "Currently equipped." : owned ? "Single-click to preview; double-click to equip." : "Single-click to preview; double-click to buy and equip.",
            BadgeText: equipped ? "Equipped" : owned ? "Owned" : string.Empty,
            Accented: previewed,
            SecondaryColor: priceColor);
    }

    private void RefreshSelectionPane()
    {
        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is null)
            return;
        string id = CharacterDocumentEditor.ReadFeatureId(preview, _slot);
        CosmeticDefinition definition = CharacterFeatureCatalog.Shipped.ResolveDefinition(_slot, id, out _);
        bool owned = _session.IsCosmeticOwned(definition.Id);
        bool equipped = IsEquipped(definition.Id);
        CatalogueEntry entry = default;
        bool purchasable = !owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out entry) && entry.Visible &&
            entry.Kind == CatalogueEntryKind.Cosmetic && entry.HasValidPrice;
        bool affordable = !purchasable || entry.PriceMilliCredits <= _economy.BalanceMilliCredits;
        string status = equipped ? "Equipped" : owned ? "Owned preview" : "UNOWNED PREVIEW";
        _values.SetRows(
        [
            new Win98ValueRowPresentation("status", "Status", status, true),
            new Win98ValueRowPresentation("price", "Price", owned ? "—" : PriceText(definition)),
            new Win98ValueRowPresentation("balance", "Balance", ContentDisplayName.Credits(_economy.BalanceMilliCredits)),
        ]);
        _buy.Text = owned ? (equipped ? "Equipped" : "Equip") :
            purchasable ? $"Buy • {PriceText(definition)}" : "Earn in Work Mode";
        _buy.Disabled = equipped || (!owned && (!purchasable || !affordable));
        // No layer tag here: PurchaseOrEquipAsync sounds the commitment for every route in.
        UiFeedbackAudioBootstrap.Tag(_buy, layer: UiSfx.NoLayer);
        _buy.TooltipText = equipped ? "This cosmetic is currently equipped."
            : owned ? "Equip this owned cosmetic now."
            : purchasable && !affordable ? $"Costs {PriceText(definition)}. Earn more credits before buying."
            : purchasable ? $"Buy permanently for {PriceText(definition)} and equip immediately."
            : "This cosmetic is earned elsewhere and cannot be bought here.";
        bool hasColor = definition.ColorChannels.Count > 0;
        _color.Disabled = !hasColor;
        _presets.Visible = hasColor;
        _color.Color = FromRgba(CharacterDocumentEditor.ReadFeatureColor(preview, _slot));
        bool transformable = definition.TransformPolicy == CosmeticTransformPolicy.MoveAndUniformScale;
        _move.Disabled = !transformable;
        _smaller.Disabled = !transformable;
        _larger.Disabled = !transformable;
        _resetTransform.Disabled = !transformable;
        if (!transformable)
            SetMoveMode(false);
    }

    private bool IsEquipped(string cosmeticId)
    {
        CharacterDocument? working = _session.WorkingDocument;
        return working is not null &&
            string.Equals(CharacterDocumentEditor.ReadFeatureId(working, _slot), cosmeticId, StringComparison.Ordinal);
    }

    private string PriceText(CosmeticDefinition definition)
    {
        if (definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out CatalogueEntry entry))
            return ContentDisplayName.Credits(entry.PriceMilliCredits);
        return "Earned";
    }

    private string StatusText(CharacterDocument? document)
    {
        if (document is null)
            return "Select or create a character first.";
        if (_session.HasUnownedPreviews)
            return "UNOWNED PREVIEW — double-click to buy and equip; changing tabs restores the equipped item.";
        if (_session.HasOwnedPreviews)
            return "Owned preview — double-click or choose Equip to apply it.";
        if (_moveMode)
            return "Move: drag the preview or use arrows; Shift moves farther; Escape exits.";
        return _session.IsDirty ? "Unsaved changes." : "Ready.";
    }

    private void SelectCosmetic(string cosmeticId)
    {
        Handle(_session.PreviewCosmetic(_slot, cosmeticId));
    }

    private async Task ActivateCosmeticAsync(string cosmeticId)
    {
        CharacterEditorActionResult preview = _session.PreviewCosmetic(_slot, cosmeticId);
        Handle(preview);
        if (preview.Completed)
            await PurchaseOrEquipAsync();
    }

    private async Task PurchaseOrEquipAsync()
    {
        string cosmeticId = CharacterDocumentEditor.ReadFeatureId(_session.PreviewDocument!, _slot);
        bool owned = _session.IsCosmeticOwned(cosmeticId);
        if (owned)
        {
            Handle(_session.EquipPreviewedCosmetic(_slot));
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Equip);
            return;
        }

        CharacterEditorActionResult purchase = _session.BuyPreviewedCosmetic(_slot);
        Handle(purchase);
        if (!purchase.Completed)
            return;

        // Sounded here, not on the Buy button: a catalogue tile can be double-clicked straight
        // into a purchase without any button being pressed.
        UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Money);

        string? saveFailure = null;
        try
        {
            await _flushProgress();
        }
        catch (Exception error)
        {
            saveFailure = $"Purchase is owned; progress save will retry: {error.Message}";
        }

        // Buying from Studio is one clear action: ownership becomes permanent and the item
        // immediately becomes the working selection. Save still controls the character document.
        Handle(_session.EquipPreviewedCosmetic(_slot));
        if (saveFailure is not null)
            SetStatus(saveFailure);
    }

    private void Randomize()
    {
        ulong seed = unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Handle(_session.Randomize(seed));
    }

    private async System.Threading.Tasks.Task SaveAsync()
    {
        if (!_session.CanSave)
        {
            Refresh();
            return;
        }
        Handle(await _session.UseCharacterAsync());
    }

    private async System.Threading.Tasks.Task CancelAsync()
    {
        CharacterEditorActionResult result = _session.RequestClose();
        if (result.NeedsUnsavedDecision)
        {
            ShowDirtyDialog();
            return;
        }
        if (result.Completed)
            _closeImmediately();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task ResolveCloseAsync(UnsavedDecision decision)
    {
        HideDirtyDialog();
        CharacterEditorActionResult result;
        if (decision == UnsavedDecision.Save)
        {
            await _session.ResolveUnsavedAsync(UnsavedDecision.Cancel);
            result = await _session.UseCharacterAsync();
        }
        else
        {
            result = await _session.ResolveUnsavedAsync(decision);
        }
        Handle(result);
        if (result.Completed && decision != UnsavedDecision.Cancel)
            _closeImmediately();
    }

    private void ShowDirtyDialog()
    {
        if (GetTree().Root.FindChild("UnsavedChangesPrompt", true, false) is Control legacyPrompt)
            legacyPrompt.Visible = false;
        _dirtyBlocker.Visible = true;
        _dirtyDialog.Visible = true;
        _dirtyDialog.MoveToFront();
    }

    private void HideDirtyDialog()
    {
        _dirtyDialog.Visible = false;
        _dirtyBlocker.Visible = false;
    }

    private void ScaleBy(double delta)
    {
        if (!TryCurrent(out CosmeticDefinition definition, out NormalizedFeatureTransform transform) ||
            definition.TransformPolicy == CosmeticTransformPolicy.None)
            return;
        double scale = Math.Clamp(transform.Scale + delta,
            definition.TransformBounds.MinimumScale, definition.TransformBounds.MaximumScale);
        Handle(_session.SetFeatureTransform(_slot, transform with { Scale = scale }));
    }

    private void ResetTransform()
    {
        if (TryCurrent(out CosmeticDefinition definition, out _))
            Handle(_session.SetFeatureTransform(_slot, definition.DefaultTransform));
    }

    private void Nudge(Vector2 delta)
    {
        if (!TryCurrent(out CosmeticDefinition definition, out NormalizedFeatureTransform transform) ||
            definition.TransformPolicy == CosmeticTransformPolicy.None)
            return;
        var moved = new NormalizedFeatureTransform(
            Math.Clamp(transform.OffsetX + delta.X, definition.TransformBounds.MinimumOffsetX, definition.TransformBounds.MaximumOffsetX),
            Math.Clamp(transform.OffsetY + delta.Y, definition.TransformBounds.MinimumOffsetY, definition.TransformBounds.MaximumOffsetY),
            transform.Scale);
        Handle(_session.SetFeatureTransform(_slot, moved));
    }

    private bool TryCurrent(out CosmeticDefinition definition, out NormalizedFeatureTransform transform)
    {
        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is null)
        {
            definition = null!;
            transform = default;
            return false;
        }
        string id = CharacterDocumentEditor.ReadFeatureId(preview, _slot);
        definition = CharacterFeatureCatalog.Shipped.ResolveDefinition(_slot, id, out _);
        transform = CharacterDocumentEditor.ReadFeatureTransform(preview, _slot);
        return true;
    }

    private void SetMoveMode(bool enabled)
    {
        bool next = enabled && !_move.Disabled;
        if (next == _moveMode)
            return;
        if (next)
        {
            _movePreviousFocus = GetViewport().GuiGetFocusOwner();
            _previewHomeZIndex = _previewInput.ZIndex;
            _previewHomeCursor = _previewInput.MouseDefaultCursorShape;
            _previewInput.ZIndex = _moveBlocker.ZIndex + 1;
            _previewInput.MouseDefaultCursorShape = CursorShape.Move;
            _moveBlocker.Visible = true;
            _moveBlocker.GrabFocus();
        }
        else
        {
            _dragging = false;
            _previewInput.ZIndex = _previewHomeZIndex;
            _previewInput.MouseDefaultCursorShape = _previewHomeCursor;
            _moveBlocker.Visible = false;
            if (GodotObject.IsInstanceValid(_movePreviousFocus) && _movePreviousFocus!.IsVisibleInTree())
                _movePreviousFocus.GrabFocus();
            _movePreviousFocus = null;
        }
        _moveMode = next;
        _move.ButtonPressed = _moveMode;
        _move.Text = _moveMode ? "Moving…" : "Move";
        if (IsInsideTree())
            SetStatus(StatusText(_session.PreviewDocument));
    }

    /// <summary>
    /// There is exactly one status bar on screen: the Win98 frame's. The editor's own label stays
    /// with the hidden legacy panel, so the studio mirrors into it without rendering a second bar.
    /// </summary>
    private void SetStatus(string text)
    {
        _status.Text = text;
        if (!GodotObject.IsInstanceValid(_frame) && IsInsideTree())
            _frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        if (GodotObject.IsInstanceValid(_frame))
            _frame!.StatusText = text;
    }

    private void SetViewZoom(float zoom)
    {
        _viewZoom = Math.Clamp(zoom, MinimumViewZoom, MaximumViewZoom);
        ApplyView();
    }

    private void ResetView()
    {
        _viewZoom = 1.0f;
        ApplyView();
    }

    private void ApplyView()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewCamera))
            return;
        ViewFrame frame = FrameFor(_slot);
        _previewCamera.Position = new Vector3(frame.Focus.X, frame.Focus.Y, _cameraHomePosition.Z);
        _previewCamera.Size = frame.Size / _viewZoom;
    }

    private static ViewFrame FrameFor(CharacterFeatureSlot slot) => slot switch
    {
        CharacterFeatureSlot.Accessories or CharacterFeatureSlot.Tops =>
            new ViewFrame(new Vector2(0, 0), 135),
        CharacterFeatureSlot.Shoes => new ViewFrame(new Vector2(0, -55), 105),
        _ => new ViewFrame(new Vector2(0, 50), 105),
    };

    private void Handle(CharacterEditorActionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Detail) && IsInsideTree())
            SetStatus(result.Detail);
        Refresh();
    }

    private void OnBalanceChanged(long _) => Refresh();

    private static PanelContainer Pane(string name, float width) => new()
    {
        Name = name,
        CustomMinimumSize = new Vector2(width, 0),
        SizeFlagsVertical = SizeFlags.ExpandFill,
    };

    private static VBoxContainer Column(Control pane)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        pane.AddChild(margin);
        var column = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);
        return column;
    }

    private static Button Action(Control parent, string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(96, 30),
        };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static Button ViewAction(Control parent, string text, Action pressed)
    {
        Button button = Action(parent, text, pressed);
        button.CustomMinimumSize = new Vector2(0, 30);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return button;
    }

    private readonly record struct ViewFrame(Vector2 Focus, float Size);

    private static string SlotId(CharacterFeatureSlot slot) => slot.ToString().ToLowerInvariant();
    private static CharacterFeatureSlot ParseSlot(string id) => CategoryOrder.First(slot => SlotId(slot) == id);
    private static string Friendly(CharacterFeatureSlot slot) => slot == CharacterFeatureSlot.Accessories
        ? "Accessories"
        : string.Concat(slot.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));
    /// <summary>
    /// The style's own name without its category: inside the Nose strip every tile already
    /// says Nose, so the tile says Button, Triangle, Broad Oval (owner instruction 2026-08-21).
    /// The feature ID keeps its category prefix — only the label loses it.
    /// </summary>
    private static string CosmeticName(CosmeticDefinition definition) =>
        ContentDisplayName.For(definition.Id);
    private static Rgba32 ToRgba(Color color) => new(
        (byte)Math.Clamp((int)Math.Round(color.R * 255), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.G * 255), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.B * 255), 0, 255));
    private static Color FromRgba(Rgba32 color) => new(color.R / 255f, color.G / 255f, color.B / 255f);
}
