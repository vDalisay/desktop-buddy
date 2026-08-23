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
using DesktopBuddy.Domain.Presentation;
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
    private const int RecentColorCount = 3;
    private const double LikeReactionSeconds = 1.1;
    private const double LikeCheckSeconds = 2.5;
    private const float LikeReactionChance = 0.35f;
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
        ("Moss", Rgba32.Parse("#5E8C4A")), ("Plum", Rgba32.Parse("#7A4E8C")),
        ("Cream", Rgba32.Parse("#F2E2C4")),
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
    private GridContainer _recentColors = null!;
    private readonly List<Rgba32> _recent = [];
    private Button _buy = null!;
    private Button _save = null!;
    private Button _move = null!;
    private Button _smaller = null!;
    private Button _larger = null!;
    private Button _resetTransform = null!;
    private Label _name = null!;
    private Label _status = null!;
    private Label? _selectedItemName;
    private Control _previewInput = null!;
    private Camera3D _previewCamera = null!;
    private VBoxContainer _previewColumn = null!;
    private PanelContainer _viewCluster = null!;
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
    private BuddyStyleTastes _tastes = BuddyStyleTastes.None;
    private double _likeCheckSeconds;
    private double _likeReactionSeconds;
    private string _likeReactionStyleId = string.Empty;
    private OmniLight3D? _likeGlow;

    public bool IsConfigured { get; private set; }
    public bool MoveMode => _moveMode;
    public CharacterFeatureSlot SelectedSlot => _slot;
    public Button SaveAction => _save;
    public Button BuyAction => _buy;
    public Win98CategoryStrip CategoryStrip => _categories;
    public Win98CatalogGrid CatalogGrid => _catalog;
    public float ViewZoom => _viewZoom;
    /// <summary>What the buddy is fond of this visit — rolled fresh on every open.</summary>
    public BuddyStyleTastes Tastes => _tastes;
    /// <summary>The bonus the last closed Studio visit paid — the scenario oracle.</summary>
    public long LastLikedStyleBonusMilliCredits { get; private set; }
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
        // Straight under the name, so the portrait is anchored to the top of the pane and the
        // remaining actions keep the foot of it.
        _previewColumn.MoveChild(_previewInput, Math.Min(2, _previewColumn.GetChildCount() - 1));
        FloatViewCluster();
        _previewAttached = true;
        if (!GodotObject.IsInstanceValid(_previewRig))
            _previewRig = _previewInput.FindChildren("*", nameof(BuddyVisualRigView), true, false)
                .OfType<BuddyVisualRigView>()
                .FirstOrDefault();
        _previewRig?.SetPreviewFaceState(BuiltInCharacterAppearance.NeutralFaceState);
        // A fresh set of tastes every visit, so what he is fond of is worth looking for again.
        _tastes = BuddyStyleTastes.Roll(
            CharacterFeatureCatalog.Shipped,
            unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        _likeCheckSeconds = 0.0;
        _likeReactionSeconds = 0.0;
        _likeReactionStyleId = string.Empty;
        ResetView();
    }

    public void DetachPreview()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewHome))
            return;
        _previewInput.Reparent(_previewHome!);
        _previewHome!.MoveChild(_previewInput, Math.Min(_previewHomeIndex, _previewHome.GetChildCount() - 1));
        DockViewCluster();
        if (GodotObject.IsInstanceValid(_paintCanvas))
            _paintCanvas!.Visible = _paintCanvasWasVisible;
        if (_cameraStateCaptured && GodotObject.IsInstanceValid(_previewCamera))
        {
            _previewCamera.Position = _cameraHomePosition;
            _previewCamera.Size = _cameraHomeSize;
        }
        _cameraStateCaptured = false;
        PayLikedStyleBonus();
        EndLikeReaction();
        _previewRig?.ClearPreviewFaceState();
        SetMoveMode(false);
        _previewAttached = false;
        if (GodotObject.IsInstanceValid(_frame))
            _frame!.StatusText = "Ready";
    }

    /// <summary>
    /// Pays for every liked style still on the character as the Studio closes. The working
    /// document is what counts, not a preview: the bonus is for what he leaves wearing.
    /// </summary>
    private void PayLikedStyleBonus()
    {
        LastLikedStyleBonusMilliCredits = 0;
        if (!IsConfigured || _session.WorkingDocument is not CharacterDocument worn)
            return;

        int liked = _tastes.WornCount(worn);
        if (liked <= 0)
            return;

        long milli = liked * BuddyStyleTastes.CreditsPerLikedStyle;
        _economy.DepositPassive(milli);
        LastLikedStyleBonusMilliCredits = milli;
    }

    /// <summary>
    /// The buddy noticing what he is wearing: a smile and a soft glow for a moment, then back
    /// to his ordinary face. Fires when a liked style goes on and now and then while it stays
    /// on, so it reads as him being pleased rather than a status light.
    /// </summary>
    private void ProcessLikeReaction(double delta)
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewRig))
            return;

        if (_likeReactionSeconds > 0.0)
        {
            _likeReactionSeconds -= delta;
            if (GodotObject.IsInstanceValid(_likeGlow))
            {
                // Up and back down across the window, so the glow swells rather than switches.
                float progress = (float)Math.Clamp(_likeReactionSeconds / LikeReactionSeconds, 0.0, 1.0);
                _likeGlow!.LightEnergy = Mathf.Sin(progress * Mathf.Pi) * 1.6f;
            }
            if (_likeReactionSeconds <= 0.0)
                EndLikeReaction();
            return;
        }

        string worn = LikedStyleWorn();
        if (worn.Length == 0)
        {
            _likeReactionStyleId = string.Empty;
            return;
        }

        bool justPutOn = !string.Equals(worn, _likeReactionStyleId, StringComparison.Ordinal);
        _likeCheckSeconds -= delta;
        if (!justPutOn && _likeCheckSeconds > 0.0)
            return;

        _likeCheckSeconds = LikeCheckSeconds;
        _likeReactionStyleId = worn;
        // Random chance while it stays on; certain the moment it goes on, because that is the
        // player asking him what he thinks.
        if (!justPutOn && GD.Randf() > LikeReactionChance)
            return;

        BeginLikeReaction();
    }

    private string LikedStyleWorn()
    {
        if (_session.PreviewDocument is not CharacterDocument preview)
            return string.Empty;
        foreach (CharacterFeatureSlot slot in CategoryOrder)
        {
            string id = CharacterDocumentEditor.ReadFeatureId(preview, slot);
            if (_tastes.Likes(id))
                return id;
        }

        return string.Empty;
    }

    private void BeginLikeReaction()
    {
        _likeReactionSeconds = LikeReactionSeconds;
        _previewRig!.SetPreviewFaceState(FaceComposer.Compose(
            FaceExpressionCatalog.Resolve(":)"),
            blinkClosed: false,
            chewActive: false,
            chewFrame: 0,
            faceSuppressed: false,
            pupilX: 0.0f,
            pupilY: 0.0f));
        if (!GodotObject.IsInstanceValid(_likeGlow))
        {
            _likeGlow = new OmniLight3D
            {
                Name = "BuddyStudioLikeGlow",
                LightColor = new Color("FFE7A8"),
                OmniRange = 320.0f,
                LightEnergy = 0.0f,
                ShadowEnabled = false,
            };
            _previewRig!.AddChild(_likeGlow);
            _likeGlow.Position = new Vector3(0.0f, 40.0f, 90.0f);
        }

        _likeGlow!.Visible = true;
    }

    private void EndLikeReaction()
    {
        _likeReactionSeconds = 0.0;
        if (GodotObject.IsInstanceValid(_likeGlow))
        {
            _likeGlow!.LightEnergy = 0.0f;
            _likeGlow.Visible = false;
        }

        if (GodotObject.IsInstanceValid(_previewRig) && _previewAttached)
            _previewRig!.SetPreviewFaceState(BuiltInCharacterAppearance.NeutralFaceState);
    }

    private void FloatViewCluster()
    {
        if (!GodotObject.IsInstanceValid(_viewCluster) || _viewCluster.GetParent() == _previewInput)
            return;
        _viewCluster.Reparent(_previewInput, false);
        _viewCluster.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _viewCluster.OffsetLeft = 8;
        _viewCluster.OffsetTop = -74;
        _viewCluster.OffsetRight = 116;
        _viewCluster.OffsetBottom = -8;
        _viewCluster.MoveToFront();
    }

    private void DockViewCluster()
    {
        if (!GodotObject.IsInstanceValid(_viewCluster) || _viewCluster.GetParent() != _previewInput)
            return;
        _viewCluster.Reparent(_previewColumn, false);
        _viewCluster.SetAnchorsPreset(LayoutPreset.TopLeft);
        _viewCluster.OffsetLeft = _viewCluster.OffsetTop = _viewCluster.OffsetRight = _viewCluster.OffsetBottom = 0;
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

    /// <summary>
    /// Window face grey behind the whole workspace. Only the panes paint themselves, so every
    /// pixel between them — the category strip's row above all — showed whatever the editor was
    /// covering (owner report 2026-08-23). A Control's own drawing lands behind its children, so
    /// this is the whole fix: no wrapper panel, no layout change.
    /// </summary>
    public override void _Draw() =>
        DrawRect(new Rect2(Vector2.Zero, Size), Win98ThemeFactory.Face);

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
        // Styles first, then the preview: the buddy is what the player is looking at, so it
        // takes the middle, and the style strip is the tall scrolling column beside it
        // (owner instruction 2026-08-22).
        BuildCatalogPane(body);
        BuildPreviewPane(body);
        BuildInspectorPane(body);

        BuildDirtyDialog();
        BuildMoveBlocker();
        _categories.Select(SlotId(_slot), notify: false);
    }

    private void BuildPreviewPane(HBoxContainer body)
    {
        var pane = Pane("BuddyStudioPreviewPane", 420);
        pane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(pane);
        _previewColumn = Column(pane);
        _previewColumn.AddChild(new Label { Text = "Preview" });
        _name = new Label { Name = "BuddyStudioCharacterName", HorizontalAlignment = HorizontalAlignment.Center };
        _previewColumn.AddChild(_name);
        _previewInput.CustomMinimumSize = new Vector2(400, 380);
        _previewInput.SizeFlagsVertical = SizeFlags.ExpandFill;
        _previewInput.MouseFilter = MouseFilterEnum.Stop;

        // View and size controls ride in the preview's lower-left corner, the same cluster
        // Paint Buddy puts its turn/zoom controls in (owner instruction 2026-08-22). It is
        // built docked in the column and floats over the preview once that is attached, so
        // every control exists and keeps its node name whether the preview is here or not.
        _viewCluster = new PanelContainer
        {
            Name = "BuddyStudioViewCluster",
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 100,
        };
        _viewCluster.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        _previewColumn.AddChild(_viewCluster);
        var clusterRows = new VBoxContainer
        {
            Name = "BuddyStudioViewClusterRows",
            MouseFilter = MouseFilterEnum.Pass,
        };
        clusterRows.AddThemeConstantOverride("separation", 2);
        _viewCluster.AddChild(clusterRows);

        var view = new HBoxContainer { Name = "BuddyStudioViewActions" };
        view.AddThemeConstantOverride("separation", 2);
        clusterRows.AddChild(view);
        Button zoomOut = ClusterAction(view, PaintToolIconProvider.ZoomOut,
            "Show more of the buddy portrait.", () => SetViewZoom(_viewZoom - ViewZoomStep));
        zoomOut.Name = "BuddyStudioZoomOut";
        Button zoomIn = ClusterAction(view, PaintToolIconProvider.ZoomIn,
            "Move closer to the selected area.", () => SetViewZoom(_viewZoom + ViewZoomStep));
        zoomIn.Name = "BuddyStudioZoomIn";
        Button resetView = ClusterAction(view, PaintToolIconProvider.ResetView,
            "Restore the selected category's default portrait framing.", ResetView);
        resetView.Name = "BuddyStudioResetView";

        var transform = new HBoxContainer { Name = "BuddyStudioTransformActions" };
        transform.AddThemeConstantOverride("separation", 2);
        clusterRows.AddChild(transform);
        _smaller = ClusterAction(transform, PaintToolIconProvider.Shrink,
            "Make the selected cosmetic smaller.", () => ScaleBy(-0.05));
        _smaller.Name = "BuddyStudioSmaller";
        _larger = ClusterAction(transform, PaintToolIconProvider.Enlarge,
            "Make the selected cosmetic larger.", () => ScaleBy(0.05));
        _larger.Name = "BuddyStudioLarger";

        // What is left over sits under the preview, along the foot of the pane.
        var actions = new HBoxContainer
        {
            Name = "BuddyStudioPreviewActions",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        actions.AddThemeConstantOverride("separation", 4);
        _previewColumn.AddChild(actions);
        _move = Action(actions, "Move", () => SetMoveMode(!_moveMode));
        _move.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _move.Name = "BuddyStudioMove";
        _resetTransform = Action(actions, "Reset", ResetTransform);
        _resetTransform.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _resetTransform.Name = "BuddyStudioReset";
        Button random = Action(actions, "Randomize", Randomize);
        random.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        random.Name = "BuddyStudioRandomize";
        random.TooltipText = "Choose from free and owned cosmetics only.";
    }

    /// <summary>One square icon button of the floating view cluster.</summary>
    private static Button ClusterAction(Control parent, string icon, string tooltip, Action pressed)
    {
        var button = new Button
        {
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(30, 28),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        PaintToolIconProvider.Apply(button, icon, string.Empty, tooltip);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private void BuildCatalogPane(HBoxContainer body)
    {
        // Two tiles wide (owner instruction 2026-08-22): 122 per tile, the grid gap, the
        // column margins and room for the scrollbar.
        var pane = Pane("BuddyStudioCatalogPane", 288);
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
        _selectedItemName = new Label
        {
            Name = "BuddyStudioSelectedItemName",
            Text = "Style",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _selectedItemName.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_selectedItemName);

        // Colour is its own section at the top: a header row carrying the custom-colour
        // picker, then the swatches. The picker is the paint bucket the Paint Buddy and Paint
        // Room windows use, not a full-width colour bar (owner instruction 2026-08-22).
        var colorHeader = new HBoxContainer { Name = "BuddyStudioColorHeader" };
        column.AddChild(colorHeader);
        colorHeader.AddChild(new Label { Text = "Color", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _color = new ColorPickerButton
        {
            Name = "BuddyStudioColor",
            CustomMinimumSize = new Vector2(34, 30),
        };
        _color.ColorChanged += color =>
        {
            if (_refreshing)
                return;
            Rgba32 chosen = ToRgba(color);
            Handle(_session.SetFeatureColor(_slot, chosen));
            RememberColor(chosen);
        };
        colorHeader.AddChild(_color);
        PaintBucketIcon(_color);

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
            preset.Pressed += () =>
            {
                Handle(_session.SetFeatureColor(_slot, captured));
                RememberColor(captured);
            };
            _presets.AddChild(preset);
        }

        // The last three colours the player actually applied, so a mixed colour can be reused
        // on another feature without opening the picker again (owner instruction 2026-08-22).
        column.AddChild(new Label { Text = "Recently used" });
        _recentColors = new GridContainer
        {
            Name = "BuddyStudioRecentColors",
            Columns = RecentColorCount,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddChild(_recentColors);
        RefreshRecentColors();

        // Everything about the transaction sits together at the foot of the pane, directly
        // above Save and Exit.
        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _values = new Win98ValuePanel { Name = "BuddyStudioValues" };
        column.AddChild(_values);
        _buy = Action(column, "Buy", () => _ = PurchaseOrEquipAsync());
        _buy.Name = "BuddyStudioBuy";
        _buy.CustomMinimumSize = new Vector2(0, 34);

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

    /// <summary>Newest first, three deep, no duplicates.</summary>
    private void RememberColor(Rgba32 color)
    {
        _recent.RemoveAll(existing => existing == color);
        _recent.Insert(0, color);
        if (_recent.Count > RecentColorCount)
            _recent.RemoveRange(RecentColorCount, _recent.Count - RecentColorCount);
        RefreshRecentColors();
    }

    private void RefreshRecentColors()
    {
        if (!GodotObject.IsInstanceValid(_recentColors))
            return;
        foreach (Node child in _recentColors.GetChildren())
        {
            _recentColors.RemoveChild(child);
            child.QueueFree();
        }
        for (int index = 0; index < RecentColorCount; index++)
        {
            var swatch = new Button
            {
                Name = $"BuddyStudioRecentColor{index + 1}",
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(68, 28),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Disabled = index >= _recent.Count,
            };
            if (index >= _recent.Count)
            {
                swatch.TooltipText = "No colour used yet.";
                _recentColors.AddChild(swatch);
                continue;
            }

            Rgba32 captured = _recent[index];
            Color color = FromRgba(captured);
            swatch.TooltipText = $"Use {captured.ToHex()} again.";
            swatch.AddThemeStyleboxOverride("normal", Win98ThemeFactory.Raised(color, 2));
            swatch.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Raised(color.Lightened(0.18f), 2));
            swatch.AddThemeStyleboxOverride("pressed", Win98ThemeFactory.Recessed(color, 2));
            swatch.Pressed += () =>
            {
                Handle(_session.SetFeatureColor(_slot, captured));
                RememberColor(captured);
            };
            _recentColors.AddChild(swatch);
        }
    }

    /// <summary>
    /// The same overlay the Paint windows put on their colour wheel: a ColorPickerButton paints
    /// its swatch over the whole button after the button draws, so the glyph has to be a child.
    /// </summary>
    private static void PaintBucketIcon(ColorPickerButton picker)
    {
        var face = new PanelContainer { Name = "BuddyStudioColorFace", MouseFilter = MouseFilterEnum.Ignore };
        face.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        picker.AddChild(face);
        face.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var icon = new TextureRect
        {
            Name = "BuddyStudioColorIcon",
            Texture = GD.Load<Texture2D>("res://assets/ui/win98/paint_bucket_brushes.svg"),
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        picker.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 4;
        icon.OffsetRight = icon.OffsetBottom = -4;
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
        _moveBlocker.TooltipText =
            "Drag the portrait to move it, scroll to resize it; click outside it or press Escape to finish.";
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
            // The wheel resizes what is being moved, so a cosmetic can be placed and sized in
            // one gesture instead of reaching back out to the buttons (owner instruction
            // 2026-08-22). Same step and the same bounded seam the size buttons use.
            if (overPreview && button.Pressed && button.ButtonIndex is
                MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ScaleBy(button.ButtonIndex == MouseButton.WheelUp ? 0.05 : -0.05);
                _moveBlocker.AcceptEvent();
                return;
            }
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
        // A free default read as an unlabelled tile next to priced ones, so the one style that
        // puts the plain buddy back was easy to miss entirely (owner report 2026-08-22).
        string secondary = definition.IsFreeDefault ? "Free" : owned ? string.Empty : PriceText(definition);
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
            BadgeText: equipped ? "Equipped" : definition.IsFreeDefault ? "Default" : owned ? "Owned" : string.Empty,
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
        // Money is coloured the way the Inventory panel colours it (owner instruction
        // 2026-08-21): the balance is always money-green, and the price answers the only
        // question the player is asking — can I afford this right now.
        Color moneyGreen = Color.Color8(0, 112, 0);
        Color unaffordableRed = Color.Color8(192, 0, 0);
        _values.SetRows(
        [
            new Win98ValueRowPresentation("status", "Status", status, true),
            new Win98ValueRowPresentation(
                "price",
                "Price",
                owned ? "—" : PriceText(definition),
                ValueColor: owned ? null : affordable ? moneyGreen : unaffordableRed),
            new Win98ValueRowPresentation(
                "balance",
                "Balance",
                ContentDisplayName.Credits(_economy.BalanceMilliCredits),
                ValueColor: moneyGreen),
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
        // The ears sit at the widest part of the head and the default head framing cropped
        // them off the sides (owner report 2026-08-21): two zoom-out steps' worth of room.
        CharacterFeatureSlot.Ears => new ViewFrame(new Vector2(0, 50), 164),
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
