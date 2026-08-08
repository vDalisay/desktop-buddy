using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Economy;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>Buddy-specific controller for the shared Win98 customization controls.</summary>
public partial class BuddyStudioWorkspace : VBoxContainer
{
    private static readonly CharacterFeatureSlot[] CategoryOrder =
    [
        CharacterFeatureSlot.Face, CharacterFeatureSlot.Hair, CharacterFeatureSlot.Brows,
        CharacterFeatureSlot.Eyes, CharacterFeatureSlot.Nose, CharacterFeatureSlot.Mouth,
        CharacterFeatureSlot.Ears, CharacterFeatureSlot.Accessories, CharacterFeatureSlot.Glasses,
        CharacterFeatureSlot.Headwear, CharacterFeatureSlot.Tops, CharacterFeatureSlot.Shoes,
    ];

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
    private Label _name = null!;
    private Label _status = null!;
    private Control _previewInput = null!;
    private VBoxContainer _previewColumn = null!;
    private Node? _previewHome;
    private int _previewHomeIndex;
    private Control? _paintCanvas;
    private bool _paintCanvasWasVisible;
    private bool _previewAttached;
    private Control _dirtyBlocker = null!;
    private PanelContainer _dirtyDialog = null!;
    private CharacterFeatureSlot _slot = CharacterFeatureSlot.Face;
    private bool _refreshing;
    private bool _moveMode;

    public bool IsConfigured { get; private set; }
    public bool MoveMode => _moveMode;
    public CharacterFeatureSlot SelectedSlot => _slot;
    public Button SaveAction => _save;
    public Button BuyAction => _buy;
    public Win98CategoryStrip CategoryStrip => _categories;
    public Win98CatalogGrid CatalogGrid => _catalog;

    public void AttachPreview()
    {
        if (_previewAttached || !GodotObject.IsInstanceValid(_previewColumn))
            return;
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
    }

    public void DetachPreview()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewHome))
            return;
        _previewInput.Reparent(_previewHome!);
        _previewHome!.MoveChild(_previewInput, Math.Min(_previewHomeIndex, _previewHome.GetChildCount() - 1));
        if (GodotObject.IsInstanceValid(_paintCanvas))
            _paintCanvas!.Visible = _paintCanvasWasVisible;
        _previewAttached = false;
    }

    public void Configure(
        CharacterEditorSession session,
        EconomyService economy,
        Control preview,
        Action closeImmediately,
        Func<Task>? flushProgress = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Buddy Studio must be configured before entering the tree.");
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _previewInput = preview ?? throw new ArgumentNullException(nameof(preview));
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
            Nudge(direction * (key.ShiftPressed ? 0.05f : 0.01f));
            GetViewport().SetInputAsHandled();
        }
    }

    public void SelectCategory(CharacterFeatureSlot slot)
    {
        if (!CategoryOrder.Contains(slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        _slot = slot;
        _categories.Select(SlotId(slot), notify: false);
        SetMoveMode(false);
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

        var actions = new HBoxContainer { Name = "BuddyStudioActions" };
        actions.AddThemeConstantOverride("separation", 6);
        AddChild(actions);
        _status = new Label
        {
            Name = "BuddyStudioStatus",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        actions.AddChild(_status);
        _save = Action(actions, "Save", () => _ = SaveAsync());
        _save.Name = "BuddyStudioSave";
        _save.TooltipText = "Save this character (Ctrl+S).";
        Button cancel = Action(actions, "Cancel", () => _ = CancelAsync());
        cancel.Name = "BuddyStudioCancel";

        BuildDirtyDialog();
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
        _previewInput.GuiInput += OnPreviewInput;

        var transform = new GridContainer
        {
            Name = "BuddyStudioTransformActions",
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _previewColumn.AddChild(transform);
        Button smaller = Action(transform, "Smaller", () => ScaleBy(-0.05));
        smaller.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        smaller.Name = "BuddyStudioSmaller";
        Button larger = Action(transform, "Larger", () => ScaleBy(0.05));
        larger.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        larger.Name = "BuddyStudioLarger";
        _move = Action(transform, "Move", () => SetMoveMode(!_moveMode));
        _move.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _move.Name = "BuddyStudioMove";
        Button reset = Action(transform, "Reset", ResetTransform);
        reset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        reset.Name = "BuddyStudioReset";
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
        _catalog = new Win98CatalogGrid { Name = "BuddyStudioCatalog" };
        _catalog.ConfigureTileSize(122, 142);
        _catalog.SelectionChanged += SelectCosmetic;
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
        _presets = new GridContainer { Name = "BuddyStudioColorPresets", Columns = 3 };
        column.AddChild(_presets);
        foreach ((string name, Rgba32 color) in Palette)
        {
            Rgba32 captured = color;
            var preset = new Button
            {
                Text = name,
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(68, 32),
                TooltipText = $"Use {name} ({color.ToHex()}).",
            };
            preset.Pressed += () => Handle(_session.SetFeatureColor(_slot, captured));
            _presets.AddChild(preset);
        }
        _values = new Win98ValuePanel { Name = "BuddyStudioValues" };
        column.AddChild(_values);
        _buy = Action(column, "Buy", () => _ = PurchaseOrEquipAsync());
        _buy.Name = "BuddyStudioBuy";
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
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Save", () => _ = ResolveCloseAsync(UnsavedDecision.Save));
        Win98Dialog.Action(actions, "Discard", () => _ = ResolveCloseAsync(UnsavedDecision.Discard));
        Win98Dialog.Action(actions, "Keep Editing", HideDirtyDialog);
        _dirtyBlocker.AddChild(_dirtyDialog);
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
            _save.Disabled = !_session.CanSave || !_session.IsDirty;
            _save.TooltipText = !_session.CanSave
                ? "Buy or deselect previewed cosmetics before saving."
                : "Save this character (Ctrl+S).";
            RefreshCatalog();
            RefreshSelectionPane();
            _status.Text = _session.LastError ?? StatusText(document);
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
        string secondary = owned ? (definition.IsFreeDefault ? "Free" : "Owned") : PriceText(definition);
        return new Win98CatalogItemPresentation(
            definition.Id,
            CosmeticName(definition),
            secondary,
            BuddyStudioThumbnailCache.For(definition),
            Tooltip: owned ? "Available to save." : "Preview only until acquired.",
            BadgeText: owned ? "Owned" : "Preview");
    }

    private void RefreshSelectionPane()
    {
        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is null)
            return;
        string id = CharacterDocumentEditor.ReadFeatureId(preview, _slot);
        CosmeticDefinition definition = CharacterFeatureCatalog.Shipped.ResolveDefinition(_slot, id, out _);
        bool owned = _session.IsCosmeticOwned(definition.Id);
        string equippedId = CharacterDocumentEditor.ReadFeatureId(_session.WorkingDocument!, _slot);
        bool equipped = string.Equals(equippedId, definition.Id, StringComparison.Ordinal) &&
            !_session.HasOwnedPreview(_slot) && !_session.HasUnownedPreview(_slot);
        CatalogueEntry entry = default;
        bool purchasable = !owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out entry) && entry.Visible &&
            entry.Kind == CatalogueEntryKind.Cosmetic && entry.HasValidPrice;
        bool affordable = !purchasable || entry.PriceMilliCredits <= _economy.BalanceMilliCredits;
        _values.SetRows(
        [
            new Win98ValueRowPresentation("status", "Status", owned ? "Owned" : "Preview", true),
            new Win98ValueRowPresentation("price", "Price", owned ? "—" : PriceText(definition)),
            new Win98ValueRowPresentation("balance", "Balance", ContentDisplayName.Credits(_economy.BalanceMilliCredits)),
        ]);
        _buy.Text = owned ? (equipped ? "Equipped" : "Equip") : purchasable ? "Buy" : "Earned";
        _buy.Disabled = equipped || (!owned && (!purchasable || !affordable));
        _buy.TooltipText = equipped ? "This cosmetic is currently equipped."
            : owned ? "Equip this cosmetic on the working character."
            : purchasable && !affordable ? "Earn more credits before buying this cosmetic."
            : purchasable ? "Buy this cosmetic permanently; equip it with the next action."
            : "This cosmetic is earned elsewhere and cannot be bought here.";
        bool hasColor = definition.ColorChannels.Count > 0;
        _color.Disabled = !hasColor;
        _presets.Visible = hasColor;
        _color.Color = FromRgba(CharacterDocumentEditor.ReadFeatureColor(preview, _slot));
        bool transformable = definition.TransformPolicy == CosmeticTransformPolicy.MoveAndUniformScale;
        _move.Disabled = !transformable;
        if (!transformable)
            SetMoveMode(false);
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
            return "Previewing an unowned cosmetic — Save is unavailable.";
        if (_session.HasOwnedPreviews)
            return "Previewing an owned cosmetic — choose Equip to apply it.";
        if (_moveMode)
            return "Move: drag the preview or use arrows; Shift moves farther; Escape exits.";
        return _session.IsDirty ? "Unsaved changes." : "Ready.";
    }

    private void SelectCosmetic(string cosmeticId) => Handle(_session.PreviewCosmetic(_slot, cosmeticId));

    private async Task PurchaseOrEquipAsync()
    {
        string cosmeticId = CharacterDocumentEditor.ReadFeatureId(_session.PreviewDocument!, _slot);
        bool owned = _session.IsCosmeticOwned(cosmeticId);
        CharacterEditorActionResult result = owned
            ? _session.EquipPreviewedCosmetic(_slot)
            : _session.BuyPreviewedCosmetic(_slot);
        Handle(result);
        string? saveFailure = null;
        if (result.Completed && !owned)
        {
            try
            {
                await _flushProgress();
            }
            catch (Exception error)
            {
                saveFailure = $"Purchase is owned; progress save will retry: {error.Message}";
            }
        }
        Refresh();
        if (saveFailure is not null)
            _status.Text = saveFailure;
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
        Handle(await _session.SaveAsync());
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
        CharacterEditorActionResult result = await _session.ResolveUnsavedAsync(decision);
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
        _moveMode = enabled && !_move.Disabled;
        _move.ButtonPressed = _moveMode;
        _move.Text = _moveMode ? "Moving…" : "Move";
        if (IsInsideTree())
            _status.Text = StatusText(_session.PreviewDocument);
    }

    private void OnPreviewInput(InputEvent input)
    {
        if (!_moveMode || input is not InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion)
            return;
        Vector2 size = _previewInput.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;
        Nudge(new Vector2(motion.Relative.X / size.X * 2f, motion.Relative.Y / size.Y * 2f));
        _previewInput.AcceptEvent();
    }

    private void Handle(CharacterEditorActionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Detail) && IsInsideTree())
            _status.Text = result.Detail;
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

    private static string SlotId(CharacterFeatureSlot slot) => slot.ToString().ToLowerInvariant();
    private static CharacterFeatureSlot ParseSlot(string id) => CategoryOrder.First(slot => SlotId(slot) == id);
    private static string Friendly(CharacterFeatureSlot slot) => slot == CharacterFeatureSlot.Accessories
        ? "Accessories"
        : string.Concat(slot.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));
    private static string CosmeticName(CosmeticDefinition definition) =>
        ContentDisplayName.For(definition.Id.Replace('.', '_'));
    private static Rgba32 ToRgba(Color color) => new(
        (byte)Math.Clamp((int)Math.Round(color.R * 255), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.G * 255), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.B * 255), 0, 255));
    private static Color FromRgba(Rgba32 color) => new(color.R / 255f, color.G / 255f, color.B / 255f);
}
