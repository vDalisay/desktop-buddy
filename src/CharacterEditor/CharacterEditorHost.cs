using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Platform;
using DesktopBuddy.Shop;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Production Phase A editor host. The editor is a same-window overlay containing one
/// physics-free BuddyVisualRigView preview.
///
/// It also composes the interim dock: a top toolbar whose Shop, Tools and Settings entries
/// each open a free-floating desktop window. ponytail: that dock belongs to FR-003.2 and
/// should move to its own host once the approved dock design lands — it lives here only
/// because this is the node already composed over the sandbox.
/// </summary>
public partial class CharacterEditorHost : CanvasLayer
{
    private readonly Dictionary<CharacterPartSlot, ColorPickerButton> _partColors = [];
    private readonly Dictionary<CharacterFeatureSlot, FeatureControls> _featureControls = [];
    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private CharacterSelectionRuntime _selectionRuntime = null!;
    private CharacterEditorModeCoordinator _mode = null!;
    private CharacterEditorSession _session = null!;
    private BuddyVisualRigView _preview = null!;
    private StaticBuddyVisualTransformSource _previewSource = null!;
    private Control _editorRoot = null!;
    private UI.Win98.Win98WindowFrame? _windowFrame;
    private SettingsPanel _settingsPanel = null!;
    private ShopPanel _shopPanel = null!;
    private ToolSelectionPanel _toolPanel = null!;
    private DockWindow _shopWindow = null!;
    private DockWindow _toolWindow = null!;
    private DockWindow _settingsWindow = null!;
    private ItemList _libraryList = null!;
    private LineEdit _nameEdit = null!;
    private Label _status = null!;
    private PanelContainer _unsavedPanel = null!;
    private Button _previousPage = null!;
    private Button _nextPage = null!;
    private bool _refreshing;
    private int _page;

    public bool IsInitialized { get; private set; }
    public bool IsEditorOpen => IsInitialized && _editorRoot.Visible;
    public CharacterEditorSession Session => _session;
    public BuddyVisualRigView PreviewRig => _preview;
    public Button SettingsButton { get; private set; } = null!;
    public Button ShopButton { get; private set; } = null!;
    public Button ToolsButton { get; private set; } = null!;
    public ToolSelectionPanel Tools => _toolPanel;
    public ShopPanel Shop => _shopPanel;
    public Button OpenCharacterEditorButton { get; private set; } = null!;
    public Button NewButton { get; private set; } = null!;
    public Button DuplicateButton { get; private set; } = null!;
    public Button DeleteButton { get; private set; } = null!;
    public Button ResetButton { get; private set; } = null!;
    public Button RandomizeButton { get; private set; } = null!;
    public Button SaveButton { get; private set; } = null!;
    public Button UseButton { get; private set; } = null!;
    public Button CloseButton { get; private set; } = null!;

    public CharacterEditorActionResult RequestNewCharacterPrompt()
    {
        CharacterEditorActionResult result = _session.RequestNewCharacterPrompt();
        Handle(result);
        return result;
    }

    public void Configure(
        SandboxRoot sandbox,
        RunContext context,
        CharacterSelectionRuntime selectionRuntime)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("CharacterEditorHost must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _selectionRuntime = selectionRuntime ?? throw new ArgumentNullException(nameof(selectionRuntime));
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready() => CallDeferred(MethodName.InitializeDeferred);

    public async Task OpenEditorAsync()
    {
        if (!IsInitialized || IsEditorOpen)
            return;
        _settingsWindow.Hide();
        _mode.Enter();
        _editorRoot.Visible = true;
        SetMoneyHudVisible(false);
        await _session.RefreshPageAsync(_page * 24, 24);
        if (_session.WorkingDocument is null && _session.CurrentPage.Count > 0)
            await _session.SelectAsync(_session.CurrentPage[0].CharacterId);
        RefreshAll();
    }

    public void CloseEditorImmediately()
    {
        if (!IsEditorOpen)
            return;
        _unsavedPanel.Visible = false;
        _editorRoot.Visible = false;
        SetMoneyHudVisible(true);
        _mode.Exit();
        RefreshDockHitRegions();
    }

    private async void InitializeDeferred()
    {
        for (int frame = 0; frame < 120 && _selectionRuntime.Coordinator is null; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (_context.Characters is null || _selectionRuntime.Coordinator is null)
        {
            GD.PushError("Character editor could not initialize its character services.");
            return;
        }

        BuildPreview();
        _mode = new CharacterEditorModeCoordinator(
            _sandbox.Window,
            _sandbox.Shell,
            _sandbox.Lifecycle);
        var library = new CharacterLibraryIndex(
            new CharacterFileSystem(),
            _context.Characters.Paths.Root);
        _session = new CharacterEditorSession(
            _context.Characters,
            library,
            _selectionRuntime.Coordinator,
            _preview,
            economy: _context.Economy);
        _session.Changed += RefreshAll;
        _session.LibraryChanged += RefreshLibrary;
        _session.CloseResolved += closed =>
        {
            if (closed)
                CloseEditorImmediately();
        };

        BuildUi();
        IsInitialized = true;
        RefreshAll();
        CallDeferred(MethodName.RefreshDockHitRegions);
    }

    private void BuildPreview()
    {
        _previewSource = new StaticBuddyVisualTransformSource(
            _sandbox.Buddy.Rig.Profile,
            Vector2.Zero);
        _preview = new BuddyVisualRigView
        {
            Name = "CharacterPreviewRig",
            ProcessMode = ProcessModeEnum.Always,
        };
        _preview.Initialize(_sandbox.Buddy.VisualProfile, _previewSource);
        // The pose is applied in BuildEditingArea, once the rig is inside the tree:
        // global transforms are meaningless — and log an error each — before that.
    }

    private void BuildUi()
    {
        var root = new Control { Name = "CharacterEditorUiRoot" };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        BuildDock(root);
        BuildEditor(root);
        BuildUnsavedPrompt(root);
    }

    private void BuildDock(Control root)
    {
        // A slim toolbar pinned to the top of the buddy box: the three entries sit in a row
        // rather than stacking over the buddy.
        var dockMargin = new MarginContainer
        {
            Name = "FloatingDock",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dockMargin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        dockMargin.OffsetLeft = 8;
        dockMargin.OffsetTop = 8;
        dockMargin.OffsetRight = -8;
        dockMargin.OffsetBottom = 40;
        root.AddChild(dockMargin);

        var dock = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        dock.AddThemeConstantOverride("separation", 6);
        dockMargin.AddChild(dock);

        // Every dock panel is a free-floating desktop window, so none of them are clipped to
        // the buddy box and the player can park them anywhere.
        _shopPanel = new ShopPanel();
        _shopPanel.Configure(_context.Progress, _context.Economy, CatalogueLoader.Catalogue, _sandbox.Pipeline);
        _shopWindow = OpenableWindow("Shop", _shopPanel, _shopPanel.Refresh);

        _toolPanel = new ToolSelectionPanel();
        _toolPanel.Configure(_context.Progress, _sandbox.Pipeline, CatalogueLoader.Catalogue);
        _toolWindow = OpenableWindow("Tools", _toolPanel, _toolPanel.Refresh);

        _settingsPanel = new SettingsPanel();
        _settingsPanel.Configure();
        _settingsWindow = OpenableWindow("Settings", _settingsPanel, null);

        // Buying a tool immediately makes it selectable, so keep the picker honest.
        _shopPanel.Purchased += _toolPanel.Refresh;

        ShopButton = Button("Shop", "DockShopButton");
        ShopButton.Pressed += () => _shopWindow.Toggle(WindowAnchor(0));
        dock.AddChild(ShopButton);

        ToolsButton = Button("Tools", "DockToolsButton");
        ToolsButton.Pressed += () => _toolWindow.Toggle(WindowAnchor(1));
        dock.AddChild(ToolsButton);

        SettingsButton = Button("Settings", "DockSettingsButton");
        SettingsButton.Pressed += () => _settingsWindow.Toggle(WindowAnchor(2));
        dock.AddChild(SettingsButton);

        // Owner decision 2026-08-03: the editor gets its own bar button rather than a
        // Settings row, so entering it is one click.
        OpenCharacterEditorButton = Button("Character Editor", "DockCharacterEditorButton");
        OpenCharacterEditorButton.TooltipText = "Create and edit the buddy's appearance.";
        OpenCharacterEditorButton.Pressed += async () => await OpenEditorAsync();
        dock.AddChild(OpenCharacterEditorButton);
    }

    /// <summary>
    /// Hosts one panel in its own desktop window and registers it with the shell, so focus
    /// moving to it is understood as using the game rather than leaving it.
    /// </summary>
    private DockWindow OpenableWindow(string title, Control panel, Action? onOpening)
    {
        var window = new DockWindow();
        window.Configure(title, new Vector2I(400, 420), panel);
        if (onOpening is not null)
            window.Opening += onOpening;
        AddChild(window);
        _sandbox.Shell.RegisterOwnedWindow(window);
        return window;
    }

    /// <summary>
    /// The editor is a full-rect overlay on its own CanvasLayer, above the layer that draws the
    /// Win98 frame. Track the frame's content rect rather than assuming chrome heights: the title
    /// and status bars grow with their font and stylebox margins, and the window is resizable.
    /// </summary>
    private void AlignEditorToWindowChrome()
    {
        if (!IsInitialized || !_editorRoot.Visible)
            return;

        if (!GodotObject.IsInstanceValid(_windowFrame))
        {
            _windowFrame = GetTree().Root
                .FindChild(nameof(UI.Win98.Win98WindowFrame), true, false) as UI.Win98.Win98WindowFrame;
            if (!GodotObject.IsInstanceValid(_windowFrame))
                return;
        }

        Rect2 content = _windowFrame!.ContentViewportRect;
        if (content.Size.Y <= 0f)
            return;

        _editorRoot.OffsetTop = content.Position.Y;
        _editorRoot.OffsetBottom = content.End.Y - GetViewport().GetVisibleRect().Size.Y;
    }

    /// <summary>The credit balance belongs to gameplay: it has no meaning while painting.</summary>
    private void SetMoneyHudVisible(bool visible)
    {
        UI.MoneyHudPresenter.SuppressedByEditor = !visible;
        if (GetTree()?.Root.FindChild("MoneyHud", true, false) is Control hud)
            hud.Visible = visible;
    }

    private void BuildEditor(Control root)
    {
        _editorRoot = new PanelContainer
        {
            Name = "CharacterEditorPanel",
            Visible = false,
            ProcessMode = ProcessModeEnum.Always,
        };
        _editorRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_editorRoot);

        // No caption row: the Win98 frame's title bar already names the window.
        var main = new VBoxContainer();
        main.AddThemeConstantOverride("separation", 0);
        _editorRoot.AddChild(main);

        var body = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        main.AddChild(body);
        BuildLibrary(body);
        BuildEditingArea(body);

        var actions = new HBoxContainer();
        main.AddChild(actions);
        NewButton = AddAction(actions, "New", async () => Handle(await Immediate(_session.NewCharacter())));
        DuplicateButton = AddAction(actions, "Duplicate", async () => Handle(await Immediate(_session.Duplicate())));
        DeleteButton = AddAction(actions, "Delete", async () => Handle(await _session.DeleteAsync()));
        ResetButton = AddAction(actions, "Reset", async () => Handle(await Immediate(_session.ResetWorkingCopy())));
        RandomizeButton = AddAction(actions, "Randomize", async () =>
        {
            ulong seed = unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Handle(await Immediate(_session.Randomize(seed)));
        });
        SaveButton = AddAction(actions, "Save", async () => Handle(await _session.SaveAsync()));
        UseButton = AddAction(actions, "Use Character", async () =>
        {
            CharacterEditorActionResult result = await _session.UseCharacterAsync();
            Handle(result);
            if (result.Completed)
                CloseEditorImmediately();
        });
        CloseButton = AddAction(actions, "Close", async () => Handle(await Immediate(_session.RequestClose())));

        _status = new Label { Name = "CharacterEditorStatus" };
        main.AddChild(_status);
    }

    private void BuildLibrary(Control parent)
    {
        var libraryColumn = new VBoxContainer
        {
            Name = "CharacterLibrary",
            CustomMinimumSize = new Vector2(230, 0),
        };
        parent.AddChild(libraryColumn);
        libraryColumn.AddChild(new Label { Text = "Local Characters" });
        _libraryList = new ItemList
        {
            Name = "CharacterLibraryList",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        _libraryList.ItemSelected += async index =>
        {
            if (index < 0 || index >= _session.CurrentPage.Count)
                return;
            CharacterIndexEntry entry = _session.CurrentPage[(int)index];
            if (entry.IsEnabled)
                Handle(await _session.SelectAsync(entry.CharacterId));
        };
        UI.Win98.Win98ItemListCheck.Attach(_libraryList);
        libraryColumn.AddChild(_libraryList);
        var pager = new HBoxContainer();
        libraryColumn.AddChild(pager);
        _previousPage = AddAction(pager, "Previous", async () =>
        {
            _page = Math.Max(0, _page - 1);
            await _session.RefreshPageAsync(_page * 24, 24);
        });
        _nextPage = AddAction(pager, "Next", async () =>
        {
            _page++;
            await _session.RefreshPageAsync(_page * 24, 24);
            if (_session.CurrentPage.Count == 0)
            {
                _page = Math.Max(0, _page - 1);
                await _session.RefreshPageAsync(_page * 24, 24);
            }
        });
    }

    private void BuildEditingArea(Control parent)
    {
        var scroll = new ScrollContainer
        {
            Name = "CharacterControlsScroll",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        parent.AddChild(scroll);
        var controls = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(controls);

        _nameEdit = new LineEdit { Name = "CharacterName", PlaceholderText = "Character name" };
        _nameEdit.TextSubmitted += value =>
        {
            if (!_refreshing)
                Handle(_session.Rename(value));
        };
        controls.AddChild(_nameEdit);

        controls.AddChild(new Label { Text = "Part Colors" });
        var colors = new GridContainer { Columns = 2 };
        controls.AddChild(colors);
        foreach (CharacterPartSlot part in Enum.GetValues<CharacterPartSlot>())
        {
            colors.AddChild(new Label { Text = Friendly(part.ToString()) });
            var picker = new ColorPickerButton { Name = $"{part}Color" };
            CharacterPartSlot captured = part;
            picker.ColorChanged += color =>
            {
                if (!_refreshing)
                    Handle(_session.SetPartColor(captured, ToRgba(color)));
            };
            colors.AddChild(picker);
            _partColors[part] = picker;
        }

        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>())
        {
            controls.AddChild(new HSeparator());
            controls.AddChild(new Label { Text = Friendly(slot.ToString()) });
            var grid = new GridContainer { Columns = 2 };
            controls.AddChild(grid);
            var option = new OptionButton { Name = $"{slot}Feature" };
            string prefix = slot switch
            {
                CharacterFeatureSlot.Eyes => "eyes.",
                CharacterFeatureSlot.Brows => "brows.",
                CharacterFeatureSlot.Mouth => "mouth.",
                _ => "accent.",
            };
            foreach (string id in FeatureIds(prefix))
                option.AddItem(id);
            CharacterFeatureSlot captured = slot;
            option.ItemSelected += index =>
            {
                if (!_refreshing && index >= 0)
                    Handle(_session.SetFeatureId(captured, option.GetItemText((int)index)));
            };
            grid.AddChild(new Label { Text = "Feature" });
            grid.AddChild(option);

            SpinBox offsetX = Spin(-1.0, 1.0, 0.01);
            SpinBox offsetY = Spin(-1.0, 1.0, 0.01);
            SpinBox scale = Spin(0.75, 1.25, 0.01);
            void UpdateTransform(double _)
            {
                if (!_refreshing)
                {
                    Handle(_session.SetFeatureTransform(
                        captured,
                        new NormalizedFeatureTransform(offsetX.Value, offsetY.Value, scale.Value)));
                }
            }
            offsetX.ValueChanged += UpdateTransform;
            offsetY.ValueChanged += UpdateTransform;
            scale.ValueChanged += UpdateTransform;
            grid.AddChild(new Label { Text = "Offset X" });
            grid.AddChild(offsetX);
            grid.AddChild(new Label { Text = "Offset Y" });
            grid.AddChild(offsetY);
            grid.AddChild(new Label { Text = "Scale" });
            grid.AddChild(scale);

            var color = new ColorPickerButton { Name = $"{slot}Color" };
            color.ColorChanged += value =>
            {
                if (!_refreshing)
                    Handle(_session.SetFeatureColor(captured, ToRgba(value)));
            };
            grid.AddChild(new Label { Text = "Color" });
            grid.AddChild(color);
            _featureControls[slot] = new FeatureControls(option, offsetX, offsetY, scale, color);
        }

        var previewContainer = new SubViewportContainer
        {
            Name = "CharacterPreview",
            CustomMinimumSize = new Vector2(420, 360),
            Stretch = true,
        };
        controls.AddChild(previewContainer);
        var viewport = new SubViewport
        {
            Size = new Vector2I(420, 360),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            // Without its own World3D the preview shares the main viewport's world, so the
            // preview rig also renders into the desktop window as a second, T-posing buddy.
            OwnWorld3D = true,
        };
        previewContainer.AddChild(viewport);
        var world = new Node3D { ProcessMode = ProcessModeEnum.Always };
        viewport.AddChild(world);
        // The preview rig is built detached and has no parent yet, so this is its first
        // entry into the tree — Reparent would fail and leave it orphaned and invisible.
        world.AddChild(_preview);
        ApplyStaticPreviewPose();
        var camera = new Camera3D
        {
            Position = new Vector3(0, 0, 600),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 400,
            Current = true,
        };
        world.AddChild(camera);
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -20, 0) });
    }

    private void BuildUnsavedPrompt(Control root)
    {
        _unsavedPanel = new PanelContainer
        {
            Name = "UnsavedChangesPrompt",
            Visible = false,
            ProcessMode = ProcessModeEnum.Always,
        };
        _unsavedPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _unsavedPanel.OffsetLeft = -180;
        _unsavedPanel.OffsetTop = -80;
        _unsavedPanel.OffsetRight = 180;
        _unsavedPanel.OffsetBottom = 80;
        root.AddChild(_unsavedPanel);
        var box = new VBoxContainer();
        _unsavedPanel.AddChild(box);
        box.AddChild(new Label
        {
            Text = "Save changes before continuing?",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        AddAction(buttons, "Save", async () => await ResolveUnsaved(UnsavedDecision.Save));
        AddAction(buttons, "Discard", async () => await ResolveUnsaved(UnsavedDecision.Discard));
        AddAction(buttons, "Cancel", async () => await ResolveUnsaved(UnsavedDecision.Cancel));
    }

    private void RefreshAll()
    {
        if (!IsInitialized || _session is null)
            return;
        _refreshing = true;
        try
        {
            CharacterDocument? document = _session.WorkingDocument;
            _nameEdit.Text = document?.DisplayName ?? string.Empty;
            if (document is not null)
            {
                foreach ((CharacterPartSlot part, ColorPickerButton picker) in _partColors)
                    picker.Color = FromRgba(CharacterDocumentEditor.ReadPartColor(document, part));
                foreach ((CharacterFeatureSlot slot, FeatureControls controls) in _featureControls)
                {
                    string id = CharacterDocumentEditor.ReadFeatureId(document, slot);
                    int index = Enumerable.Range(0, controls.Option.ItemCount)
                        .FirstOrDefault(i => string.Equals(
                            controls.Option.GetItemText(i), id, StringComparison.Ordinal), -1);
                    if (index >= 0)
                        controls.Option.Select(index);
                    NormalizedFeatureTransform transform =
                        CharacterDocumentEditor.ReadFeatureTransform(document, slot);
                    controls.OffsetX.Value = transform.OffsetX;
                    controls.OffsetY.Value = transform.OffsetY;
                    controls.Scale.Value = transform.Scale;
                    controls.Color.Color = FromRgba(
                        CharacterDocumentEditor.ReadFeatureColor(document, slot));
                }
            }
            bool hasDocument = document is not null;
            SaveButton.Disabled = !hasDocument || !_session.IsDirty;
            UseButton.Disabled = !hasDocument;
            DuplicateButton.Disabled = !hasDocument;
            DeleteButton.Disabled = !hasDocument;
            ResetButton.Disabled = !hasDocument || !_session.IsDirty;
            RandomizeButton.Disabled = !hasDocument;
            _previousPage.Disabled = _page == 0;
            _status.Text = _session.LastError ??
                (hasDocument
                    ? $"{document!.DisplayName}{(_session.IsDirty ? " • Unsaved" : " • Saved")}" 
                    : "Create or select a local character.");
            _unsavedPanel.Visible = _session.PendingAction != CharacterEditorPendingAction.None;
            RefreshLibrary();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshLibrary()
    {
        if (!IsInitialized || _libraryList is null)
            return;
        _libraryList.Clear();
        foreach (CharacterIndexEntry entry in _session.CurrentPage)
        {
            int index = _libraryList.AddItem(entry.DisplayName);
            _libraryList.SetItemDisabled(index, !entry.IsEnabled);
            _libraryList.SetItemTooltip(index, entry.Detail ?? entry.DirectoryName);
            if (entry.CharacterId == _session.SelectedCharacterId)
                _libraryList.Select(index);
        }
    }

    private void ApplyStaticPreviewPose()
    {
        BuddyVisualPartPose Pose(BuddyPartId id)
        {
            BuddyVisualTransform transform = _previewSource.ReadTransform(id);
            return new BuddyVisualPartPose(
                transform,
                WorldPlaneMapping.To3D(transform.Position),
                Vector3.Zero);
        }
        _preview.ApplyPose(new BuddyVisualPoseFrame(
            Pose(BuddyPartId.Head),
            Pose(BuddyPartId.Torso),
            Pose(BuddyPartId.LeftHand),
            Pose(BuddyPartId.RightHand),
            Pose(BuddyPartId.LeftFoot),
            Pose(BuddyPartId.RightFoot),
            0.0f,
            BuiltInCharacterAppearance.NeutralFaceState,
            string.Empty,
            0.0f));
    }

    private async Task ResolveUnsaved(UnsavedDecision decision)
    {
        _unsavedPanel.Visible = false;
        Handle(await _session.ResolveUnsavedAsync(decision));
    }

    private void Handle(CharacterEditorActionResult result)
    {
        if (result.NeedsUnsavedDecision)
            _unsavedPanel.Visible = true;
        if (!string.IsNullOrWhiteSpace(result.Detail))
            _status.Text = result.Detail;
        RefreshAll();
    }

    /// <summary>
    /// Where a dock window lands the first time it opens: beside the game window, stepped so a
    /// second one does not cover the first. After that the player's own placement is kept.
    /// </summary>
    private Vector2I WindowAnchor(int slot)
    {
        Rect2I game = _sandbox.Window.CurrentSettings.Rect;
        return new Vector2I(
            game.Position.X - 420 + (slot * 32),
            game.Position.Y + (slot * 32));
    }

    private void RefreshDockHitRegions()
    {
        if (!IsInitialized || IsEditorOpen)
            return;
        if (_workPlayControlsComposed)
        {
            // The bar lives in its own HWND with its own hit testing. Re-registering the
            // buttons' rects here would block a stale rectangle of the overlay instead.
            _sandbox.SetOverlayWorkModeHitRegions(Array.Empty<Rect2>());
            return;
        }
        // The sandbox owns the Work-Mode region list (it rebuilds it from the moving
        // buddy bodies every frame); the dock only contributes its own rectangles.
        // Only in-window controls need hit regions. The shop and tool windows are separate
        // desktop windows with their own hit testing, so they are deliberately absent.
        var regions = new List<Rect2>
        {
            ShopButton.GetGlobalRect(),
            ToolsButton.GetGlobalRect(),
            SettingsButton.GetGlobalRect(),
        };
        _sandbox.SetOverlayWorkModeHitRegions(regions);
    }

    private Button AddAction(Control parent, string text, Func<Task> action)
    {
        Button button = Button(text, text.Replace(" ", string.Empty) + "Button");
        button.Pressed += async () => await action();
        parent.AddChild(button);
        return button;
    }

    private static Button Button(string text, string name) => new()
    {
        Text = text,
        Name = name,
        FocusMode = Control.FocusModeEnum.All,
    };

    private static SpinBox Spin(double minimum, double maximum, double step) => new()
    {
        MinValue = minimum,
        MaxValue = maximum,
        Step = step,
        AllowGreater = false,
        AllowLesser = false,
    };

    private static Task<CharacterEditorActionResult> Immediate(CharacterEditorActionResult result) =>
        Task.FromResult(result);

    private static string[] FeatureIds(string prefix) =>
        typeof(CharacterFeatureIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.IsLiteral ? field.GetRawConstantValue() as string : field.GetValue(null) as string)
            .Where(value => value is not null && value.StartsWith(prefix, StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static Rgba32 ToRgba(Color color) => new(
        (byte)Math.Clamp((int)Math.Round(color.R * 255.0f), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.G * 255.0f), 0, 255),
        (byte)Math.Clamp((int)Math.Round(color.B * 255.0f), 0, 255));

    private static Color FromRgba(Rgba32 color) => new(
        color.R / 255.0f,
        color.G / 255.0f,
        color.B / 255.0f,
        1.0f);

    private static Rect2I ToRect(Rect2 rect) => new(
        (int)Math.Floor(rect.Position.X),
        (int)Math.Floor(rect.Position.Y),
        (int)Math.Ceiling(rect.Size.X),
        (int)Math.Ceiling(rect.Size.Y));

    private static string Friendly(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private sealed record FeatureControls(
        OptionButton Option,
        SpinBox OffsetX,
        SpinBox OffsetY,
        SpinBox Scale,
        ColorPickerButton Color);
}
