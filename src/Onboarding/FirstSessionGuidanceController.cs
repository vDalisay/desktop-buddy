using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Environment;
using DesktopBuddy.Shop;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Optional seam for owner-supplied tutorial art. Stable tutorial IDs and persistence stay owned
/// by <see cref="FirstSessionGuidanceController"/>; replacing the helper art never changes flow.
/// </summary>
public interface ITutorialCharacterPresenter
{
    void Present(string stepId, string text);
    void Dismiss();
}

/// <summary>
/// Concise action-driven first-session guidance plus permanent contextual Help. The tutorial
/// observes real runtime state rather than intercepting gameplay: opening a workspace merely gets
/// the player there; the required paint/save/equip/drag action must actually succeed before the
/// durable v2 record advances.
/// </summary>
public partial class FirstSessionGuidanceController : CanvasLayer
{
    private const string Category = "Onboarding";
    private const int TutorialWidth = 360;
    private const int WorkGuideWidth = 380;
    private const int WorkGuideHeight = 170;

    private static readonly Dictionary<string, HelpDefinition> ExplicitHelp =
        new(StringComparer.Ordinal)
        {
            ["Win98CommandBar"] = new("Top bar", "Open Shop, Tools, Paint, Work and other main game workspaces here."),
            ["Win98BalanceLabel"] = new("Credits", "Your current credits. Earn them by playing with Buddy and in Work Mode, then spend them on tools and customization."),

            ["Win98CharacterColumn"] = new("Characters", "Choose which local character you are editing. The layer panel below controls which body part receives paint."),
            ["Win98PaintLayerPanel"] = new("Layers", "Choose which body-part layer receives paint. Hidden layers cannot receive paint and return when you leave the editor."),
            ["Win98PaintToolColumn"] = new("Paint tools", "Choose a brush or eraser, change brush size, rotate the preview, undo/redo, and adjust the view."),
            ["Win98PaintViewportFrame"] = new("Paint canvas", "Draw directly on Buddy here. Your brush follows the visible 3D surface and the selected layer filter."),
            ["CharacterPaintCanvas"] = new("Paint canvas", "Draw directly on Buddy. Drag to paint; the current brush, color, size and layer determine the result."),
            ["Win98PaintColorFooter"] = new("Colors and actions", "Choose paint colors here. Save stores the character; Use Character applies it to the live Buddy; Exit leaves the editor."),
            ["PaintPresetPalette"] = new("Palette", "Pick a saved color quickly. The color-wheel button opens the full picker."),
            ["PaintPrimaryActions"] = new("Character actions", "Save stores changes, Use Character applies this character, Reset restores the saved version, and Exit leaves Paint Buddy."),

            ["PaintBackgroundPanel"] = new("Paint Background", "Paint the room backdrop with the same simple paint workflow. Save and Exit keeps the result."),
            ["PaintToolGrid"] = new("Background tools", "Choose Brush, Pen, Spray, Fill, Eraser, Pick Color, shapes, or Undo."),
            ["PaintBrushSizeRow"] = new("Brush size", "Change how large the active background brush is."),
            ["PaintBackgroundPalettePanel"] = new("Background palette", "Choose the active background color, add a custom swatch, or open the full color picker."),
            ["EnvironmentBackgroundInputBlocker"] = new("Background canvas", "Paint directly on the visible room. The tool panel hides while you drag so it does not cover the canvas."),

            ["BuddyStudioCategories"] = new("Categories", "Choose which part of Buddy you want to customize, such as eyes, glasses, headwear, tops, or shoes."),
            ["BuddyStudioPreviewPane"] = new("Preview", "Preview the selected cosmetic here. Supported cosmetics can be moved or resized before saving."),
            ["BuddyStudioCatalogPane"] = new("Styles", "Single-click a style to preview it. Owned styles can be equipped; unowned styles show their price."),
            ["BuddyStudioInspectorPane"] = new("Color and ownership", "Change supported colors and see whether the previewed style is owned, equipped, or available to buy."),
            ["BuddyStudioBuy"] = new("Buy / Equip", "Buy an unowned style permanently, or equip a style you already own."),
            ["BuddyStudioActions"] = new("Studio actions", "Save applies the current character changes. Exit leaves Buddy Studio and asks about unsaved changes when needed."),

            ["WorkCompanionRoot"] = new("Work companion", "Drag Buddy or the computer to move the companion. Double-click Buddy to return to Play Mode."),
            ["WorkCrtCounter"] = new("Work counter", "Shows current-session or lifetime actions. Click the CRT to switch which counter is shown."),
            ["WorkResizeButton"] = new("Resize", "Drag this control to resize the Work companion window."),
            ["WorkMotionToggle"] = new("Motion", "Pause or resume Buddy's Work animations. Counters and rewards continue either way."),
            ["WorkExitButton"] = new("Exit Work Mode", "Return to normal Play Mode. Double-clicking Buddy does the same thing."),
        };

    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private TutorialProgressState _tutorial = null!;
    private ITutorialCharacterPresenter? _characterPresenter;

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Label _body = null!;
    private Button _dismiss = null!;
    private Button _skip = null!;
    private Button _help = null!;

    private HelpSpotlightOverlay _helpSpotlight = null!;
    private PanelContainer _helpPopup = null!;
    private Label _helpTitle = null!;
    private Label _helpBody = null!;
    private bool _helpActive;

    private Window? _workGuideWindow;
    private Label? _workGuideBody;

    private CharacterEditorHost? _editor;
    private ShopPanel? _shop;
    private WorkCompanionCoordinator? _work;
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    private BuddyStudioWorkspace? _studio;

    private bool _editorSignalsBound;
    private bool _studioSignalsBound;
    private bool _backgroundSignalsBound;
    private bool _wasGrabbing;
    private bool _wasEditorOpen;
    private bool _wasStudioOpen;
    private bool _hasSeenWorkActive;
    private bool _paintSaveRequested;
    private bool _paintUseRequested;
    private bool _backgroundSaveRequested;
    private bool _studioPurchaseObserved;
    private bool _studioSaveRequested;
    private CharacterFeatureSlot? _studioPurchasedSlot;
    private string? _studioDefaultCosmeticId;
    private Rect2I _workDragOrigin;
    private Rect2I _workResizeOrigin;

    private string? _displayedStepId;
    private string? _dismissedStepId;

    public TutorialProgressState Progress => _tutorial;
    public string? DisplayedStepId => _displayedStepId;
    public bool ContextHelpActive => _helpActive;

    public void Configure(
        SandboxRoot sandbox,
        RunContext context,
        ITutorialCharacterPresenter? characterPresenter = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("First-session guidance must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tutorial = new TutorialProgressState(context.Progress);
        _characterPresenter = characterPresenter;
        ProcessMode = ProcessModeEnum.Always;
        Layer = 220;
    }

    public override void _Ready()
    {
        BuildUi();
        BuildContextHelpUi();
        BuildWorkGuideWindow();
        _context.Progress.Changed += OnProgressChanged;

        // Existing players should not suddenly receive a first-session walkthrough because the
        // Demo learned a more precise v2 sequence. Reset Progress removes the extension record and
        // the same controller then starts naturally at Grab Buddy.
        if (!_tutorial.HasPersistedRecord &&
            _context.LoadStatus is SaveLoadStatus.Loaded or SaveLoadStatus.BackupRecovered)
        {
            if (_tutorial.Skip())
                RequestImmediateFlush();
        }

        RefreshHint();
    }

    public override void _Process(double delta)
    {
        DiscoverRuntimeNodes();
        BindActionSignals();
        AdvanceCurrentStep();

        if (_helpActive)
            RefreshContextHelp();

        string? next = _tutorial.NextIncompleteStepId;
        if (!string.Equals(next, _displayedStepId, StringComparison.Ordinal) &&
            !string.Equals(next, _dismissedStepId, StringComparison.Ordinal))
        {
            RefreshHint();
        }

        _wasEditorOpen = GodotObject.IsInstanceValid(_editor) && _editor!.IsEditorOpen;
        _wasStudioOpen = IsStudioOpen();
    }

    public override void _Input(InputEvent input)
    {
        if (!_helpActive || input is not InputEventMouseButton mouse)
            return;

        // Help mode is observational: hovering is allowed, clicking underlying gameplay/UI is not.
        // The Help button itself remains clickable so the mode can always be closed.
        if (GodotObject.IsInstanceValid(_help) && _help.GetGlobalRect().HasPoint(mouse.Position))
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (_context?.Progress is not null)
            _context.Progress.Changed -= OnProgressChanged;
        UnbindActionSignals();
        _characterPresenter?.Dismiss();
        if (GodotObject.IsInstanceValid(_workGuideWindow))
            _workGuideWindow!.QueueFree();
    }

    public bool SkipTutorial()
    {
        bool changed = _tutorial.Skip();
        if (changed)
            RequestImmediateFlush();
        _dismissedStepId = null;
        RefreshHint();
        return changed;
    }

    private void DiscoverRuntimeNodes()
    {
        _editor ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _shop ??= GetTree().Root.FindChild("ShopPanel", true, false) as ShopPanel;
        _work ??= GetTree().Root.FindChild(nameof(WorkCompanionCoordinator), true, false) as WorkCompanionCoordinator;
        _backgroundEditor ??= GetTree().Root.FindChild(nameof(EnvironmentBackgroundEditor), true, false) as EnvironmentBackgroundEditor;
        _backgroundPresenter ??= GetTree().Root.FindChild(nameof(EnvironmentBackgroundPresenter), true, false) as EnvironmentBackgroundPresenter;
        _studio ??= GetTree().Root.FindChild(nameof(BuddyStudioWorkspace), true, false) as BuddyStudioWorkspace;
    }

    private void BindActionSignals()
    {
        if (!_editorSignalsBound && GodotObject.IsInstanceValid(_editor) && _editor!.IsInitialized)
        {
            _editor.SaveButton.Pressed += OnPaintSavePressed;
            _editor.UseButton.Pressed += OnPaintUsePressed;
            _editorSignalsBound = true;
        }

        if (!_studioSignalsBound && GodotObject.IsInstanceValid(_studio) && _studio!.IsInsideTree())
        {
            _studio.SaveAction.Pressed += OnStudioSavePressed;
            _studioSignalsBound = true;
        }

        if (!_backgroundSignalsBound && GodotObject.IsInstanceValid(_backgroundEditor) &&
            _backgroundEditor!.FindChild("PaintSaveButton", true, false) is Button save)
        {
            save.Pressed += OnBackgroundSavePressed;
            _backgroundSignalsBound = true;
        }
    }

    private void UnbindActionSignals()
    {
        if (_editorSignalsBound && GodotObject.IsInstanceValid(_editor))
        {
            _editor!.SaveButton.Pressed -= OnPaintSavePressed;
            _editor.UseButton.Pressed -= OnPaintUsePressed;
        }
        if (_studioSignalsBound && GodotObject.IsInstanceValid(_studio))
            _studio!.SaveAction.Pressed -= OnStudioSavePressed;
        if (_backgroundSignalsBound && GodotObject.IsInstanceValid(_backgroundEditor) &&
            _backgroundEditor!.FindChild("PaintSaveButton", true, false) is Button save)
            save.Pressed -= OnBackgroundSavePressed;
    }

    private void AdvanceCurrentStep()
    {
        string? step = _tutorial.NextIncompleteStepId;
        if (step is null)
            return;

        bool grabbing = _sandbox.Grab.IsGrabbing;
        bool grabbedBuddy = grabbing && !_wasGrabbing && _sandbox.Grab.CurrentGrab.Target is PuppetPartBody;
        _wasGrabbing = grabbing;

        switch (step)
        {
            case TutorialStepIds.GrabBuddy when grabbedBuddy:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenInventory when GodotObject.IsInstanceValid(_shop) && _shop!.IsVisibleInTree():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.PurchaseBaseballBat when _context.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat):
                CompleteCurrent(step);
                break;

            case TutorialStepIds.EquipBaseballBat when _sandbox.Pipeline.SelectedTool == ToolId.BaseballBat:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenPaintBuddy when IsPaintBuddyOpen():
                _paintSaveRequested = false;
                _paintUseRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.PaintBuddy when IsPaintBuddyOpen() && _editor!.PaintWorkspace.IsDirty:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SavePaintBuddy when IsPaintBuddyOpen() && _paintSaveRequested && !_editor!.Session.IsDirty:
                _paintSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.UsePaintedBuddy when _paintUseRequested && _wasEditorOpen &&
                                                        GodotObject.IsInstanceValid(_editor) && !_editor!.IsEditorOpen:
                _paintUseRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenPaintBackground when GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen:
                _backgroundSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.PaintBackground when GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen &&
                                                        GodotObject.IsInstanceValid(_backgroundPresenter) && _backgroundPresenter!.Canvas.IsDirty:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SaveAndExitPaintBackground when _backgroundSaveRequested &&
                                                                   GodotObject.IsInstanceValid(_backgroundEditor) && !_backgroundEditor!.IsOpen &&
                                                                   GodotObject.IsInstanceValid(_backgroundPresenter) && !_backgroundPresenter!.Canvas.IsDirty:
                _backgroundSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenBuddyStudio when IsStudioOpen():
                _studioPurchaseObserved = false;
                _studioSaveRequested = false;
                _studioPurchasedSlot = null;
                _studioDefaultCosmeticId = null;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.BuyAndEquipStudioItem when TryCaptureStudioPurchase():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.UnequipStudioItem when HasReturnedStudioSlotToDefault():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SaveBuddyStudio when IsStudioOpen() && _studioSaveRequested && !_editor!.Session.IsDirty:
                _studioSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.ExitBuddyStudio when _wasStudioOpen && !IsStudioOpen():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.EnterWorkMode when IsWorkActive():
                _hasSeenWorkActive = true;
                _workDragOrigin = _sandbox.Window.WorkCompanionRect;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.DragWorkCompanion when IsWorkActive() &&
                                                         _sandbox.Window.WorkCompanionRect.Position != _workDragOrigin.Position:
                _workResizeOrigin = _sandbox.Window.WorkCompanionRect;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.ResizeWorkCompanion when IsWorkActive() &&
                                                           _sandbox.Window.WorkCompanionRect.Size != _workResizeOrigin.Size:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.ExitWorkMode when _hasSeenWorkActive && !IsWorkActive():
                CompleteCurrent(step);
                break;
        }
    }

    private bool IsPaintBuddyOpen() =>
        GodotObject.IsInstanceValid(_editor) && _editor!.IsEditorOpen && _editor.IsPaintMode && !IsStudioOpen();

    private bool IsStudioOpen() =>
        GodotObject.IsInstanceValid(_studio) && _studio!.IsVisibleInTree();

    private bool IsWorkActive() =>
        GodotObject.IsInstanceValid(_work) && _work!.IsActive;

    private bool TryCaptureStudioPurchase()
    {
        if (!_studioPurchaseObserved || !IsStudioOpen() || !GodotObject.IsInstanceValid(_editor) ||
            _editor!.Session.WorkingDocument is not CharacterDocument document)
        {
            return false;
        }

        CharacterFeatureSlot slot = _studio!.SelectedSlot;
        CosmeticDefinition? defaultDefinition = _editor.Session.FeatureCatalog
            .GetDefinitions(slot)
            .FirstOrDefault(static definition => definition.IsFreeDefault);
        if (defaultDefinition is null)
            return false;

        string equipped = CharacterDocumentEditor.ReadFeatureId(document, slot);
        if (string.Equals(equipped, defaultDefinition.Id, StringComparison.Ordinal))
            return false;

        _studioPurchasedSlot = slot;
        _studioDefaultCosmeticId = defaultDefinition.Id;
        _studioPurchaseObserved = false;
        return true;
    }

    private bool HasReturnedStudioSlotToDefault()
    {
        if (!IsStudioOpen() || _studioPurchasedSlot is not CharacterFeatureSlot slot ||
            string.IsNullOrWhiteSpace(_studioDefaultCosmeticId) ||
            _editor!.Session.WorkingDocument is not CharacterDocument document)
        {
            return false;
        }
        string equipped = CharacterDocumentEditor.ReadFeatureId(document, slot);
        return string.Equals(equipped, _studioDefaultCosmeticId, StringComparison.Ordinal);
    }

    private void OnProgressChanged(ProgressChange change)
    {
        if (change == ProgressChange.BalanceChanged &&
            string.Equals(_tutorial.NextIncompleteStepId, TutorialStepIds.EarnCredits, StringComparison.Ordinal))
        {
            CompleteCurrent(TutorialStepIds.EarnCredits);
        }
        else if (change == ProgressChange.ContentPurchased && IsStudioOpen())
        {
            _studioPurchaseObserved = true;
        }
    }

    private void OnPaintSavePressed() => _paintSaveRequested = IsPaintBuddyOpen();
    private void OnPaintUsePressed() => _paintUseRequested = IsPaintBuddyOpen();
    private void OnBackgroundSavePressed() =>
        _backgroundSaveRequested = GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen;
    private void OnStudioSavePressed() => _studioSaveRequested = IsStudioOpen();

    private void CompleteCurrent(string stepId)
    {
        if (!string.Equals(_tutorial.NextIncompleteStepId, stepId, StringComparison.Ordinal) ||
            !_tutorial.MarkCompleted(stepId))
        {
            return;
        }
        _dismissedStepId = null;
        RequestImmediateFlush();
        RefreshHint();
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "FirstSessionGuidanceRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var margin = new MarginContainer
        {
            Name = "FirstSessionGuidanceMargin",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        margin.OffsetLeft = 12;
        margin.OffsetTop = -178;
        margin.OffsetRight = 12 + TutorialWidth;
        margin.OffsetBottom = -12;
        _root.AddChild(margin);

        _panel = CreateWin98MessagePanel(
            "FirstSessionGuidancePanel",
            "Desktop Buddy Help",
            out _body,
            out HBoxContainer actions);
        _panel.CustomMinimumSize = new Vector2(TutorialWidth, 142);
        margin.AddChild(_panel);

        _dismiss = Win98Dialog.Action(actions, "Dismiss", DismissCurrent);
        _dismiss.TooltipText = "Hide this hint. Tutorial progress is not skipped.";
        _skip = Win98Dialog.Action(actions, "Skip Tutorial", () => SkipTutorial());
        _skip.TooltipText = "Stop the first-session walkthrough. The Help button remains available.";
    }

    private PanelContainer CreateWin98MessagePanel(
        string name,
        string title,
        out Label bodyLabel,
        out HBoxContainer actions)
    {
        var panel = new PanelContainer
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = Win98ThemeFactory.Create(),
        };
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        var titleBar = new PanelContainer
        {
            Name = $"{name}TitleBar",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.TitleBarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titleBar);
        var titleLabel = new Label
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        titleBar.AddChild(titleLabel);

        bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(bodyLabel);

        actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        actions.AddThemeConstantOverride("separation", 6);
        column.AddChild(actions);
        return panel;
    }

    private void BuildWorkGuideWindow()
    {
        if (DisplayServer.GetName() == "headless")
            return;

        _workGuideWindow = new Window
        {
            Name = "TutorialWorkGuideWindow",
            Title = "Desktop Buddy Help",
            Size = new Vector2I(WorkGuideWidth, WorkGuideHeight),
            Borderless = true,
            Unresizable = true,
            AlwaysOnTop = true,
            Visible = false,
        };
        AddChild(_workGuideWindow);
        _sandbox.Shell.RegisterOwnedWindow(_workGuideWindow);

        PanelContainer panel = CreateWin98MessagePanel(
            "TutorialWorkGuidePanel",
            "Desktop Buddy Help",
            out Label body,
            out HBoxContainer actions);
        _workGuideBody = body;
        _workGuideWindow.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Win98Dialog.Action(actions, "Dismiss", DismissCurrent);
        Win98Dialog.Action(actions, "Skip Tutorial", () => SkipTutorial());
        _workGuideWindow.CloseRequested += DismissCurrent;
    }

    private void PositionWorkGuideWindow()
    {
        if (!GodotObject.IsInstanceValid(_workGuideWindow))
            return;
        Rect2I workRect = _sandbox.Window.WorkCompanionRect;
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        int x = workRect.End.X + 12;
        if (x + WorkGuideWidth > usable.End.X)
            x = workRect.Position.X - WorkGuideWidth - 12;
        x = Math.Clamp(x, usable.Position.X, Math.Max(usable.Position.X, usable.End.X - WorkGuideWidth));
        int y = Math.Clamp(
            workRect.Position.Y,
            usable.Position.Y,
            Math.Max(usable.Position.Y, usable.End.Y - WorkGuideHeight));
        _workGuideWindow!.Position = new Vector2I(x, y);
    }

    private void DismissCurrent()
    {
        _dismissedStepId = _displayedStepId;
        _displayedStepId = null;
        _panel.Visible = false;
        HideWorkGuide();
        _characterPresenter?.Dismiss();
    }

    private void RefreshHint()
    {
        string? stepId = _tutorial.NextIncompleteStepId;
        _displayedStepId = stepId;
        if (_helpActive || stepId is null || string.Equals(stepId, _dismissedStepId, StringComparison.Ordinal))
        {
            _panel.Visible = false;
            HideWorkGuide();
            _characterPresenter?.Dismiss();
            return;
        }

        string text = TextFor(stepId);
        if (IsWorkTutorialStep(stepId) && IsWorkActive() && GodotObject.IsInstanceValid(_workGuideWindow))
        {
            _panel.Visible = false;
            _characterPresenter?.Dismiss();
            _workGuideBody!.Text = text;
            PositionWorkGuideWindow();
            _workGuideWindow!.Visible = true;
            return;
        }

        HideWorkGuide();
        _body.Text = text;
        _panel.Visible = true;
        _characterPresenter?.Present(stepId, text);
    }

    private void HideWorkGuide()
    {
        if (GodotObject.IsInstanceValid(_workGuideWindow))
            _workGuideWindow!.Visible = false;
    }

    private static bool IsWorkTutorialStep(string stepId) => stepId is
        TutorialStepIds.EnterWorkMode or TutorialStepIds.DragWorkCompanion or
        TutorialStepIds.ResizeWorkCompanion or TutorialStepIds.ExitWorkMode;

    private static string TextFor(string stepId) => stepId switch
    {
        TutorialStepIds.GrabBuddy => "Grab Buddy and move them once.",
        TutorialStepIds.EarnCredits => "Earn some credits by interacting with Buddy.",
        TutorialStepIds.OpenInventory => "Open Shop in the top bar.",
        TutorialStepIds.PurchaseBaseballBat => "Buy the Baseball Bat.",
        TutorialStepIds.EquipBaseballBat => "Equip the Baseball Bat.",
        TutorialStepIds.OpenPaintBuddy => "Open Paint ▸ Buddy.",
        TutorialStepIds.PaintBuddy => "Paint one mark on Buddy.",
        TutorialStepIds.SavePaintBuddy => "Save your character.",
        TutorialStepIds.UsePaintedBuddy => "Choose Use Character to apply it and return.",
        TutorialStepIds.OpenPaintBackground => "Open Paint ▸ Background.",
        TutorialStepIds.PaintBackground => "Paint one mark on the background.",
        TutorialStepIds.SaveAndExitPaintBackground => "Choose Save and Exit.",
        TutorialStepIds.OpenBuddyStudio => "Open Buddy Studio.",
        TutorialStepIds.BuyAndEquipStudioItem => "Buy and equip one style. Glasses are a quick option.",
        TutorialStepIds.UnequipStudioItem => "Switch that category back to its free/default style and equip it.",
        TutorialStepIds.SaveBuddyStudio => "Save your Buddy Studio changes.",
        TutorialStepIds.ExitBuddyStudio => "Exit Buddy Studio.",
        TutorialStepIds.EnterWorkMode => "Enter Work Mode from the top bar.",
        TutorialStepIds.DragWorkCompanion => "Drag the Work companion to a new position.",
        TutorialStepIds.ResizeWorkCompanion => "Resize the Work companion once.",
        TutorialStepIds.ExitWorkMode => "Double-click Buddy or press X to return to Play Mode.",
        _ => string.Empty,
    };

    private void BuildContextHelpUi()
    {
        _help = new Button
        {
            Name = "ContextHelpButton",
            Text = "Help",
            TooltipText = "Explain the part of the current screen you hover over.",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(58, 24),
            Theme = Win98ThemeFactory.Create(),
        };
        _help.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _help.OffsetLeft = -74;
        _help.OffsetTop = 30;
        _help.OffsetRight = -12;
        _help.OffsetBottom = 56;
        _help.Pressed += ToggleContextHelp;
        _root.AddChild(_help);

        _helpSpotlight = new HelpSpotlightOverlay
        {
            Name = "ContextHelpSpotlight",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _helpSpotlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_helpSpotlight);
        _root.MoveChild(_helpSpotlight, Math.Max(0, _help.GetIndex()));

        _helpPopup = CreateWin98MessagePanel(
            "ContextHelpPopup",
            "Help",
            out _helpBody,
            out HBoxContainer popupActions);
        _helpPopup.Visible = false;
        _helpPopup.CustomMinimumSize = new Vector2(330, 124);
        _helpPopup.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_helpPopup);

        // Replace the generic title text with a dynamic region title while retaining the actual
        // Win98 blue title bar created by CreateWin98MessagePanel.
        _helpTitle = _helpPopup.FindChild("ContextHelpPopupTitleBar", true, false)?.GetChildOrNull<Label>(0)
            ?? throw new InvalidOperationException("Context Help title bar was not composed.");
        popupActions.Visible = false;
        _root.MoveChild(_help, _root.GetChildCount() - 1);
    }

    private void ToggleContextHelp()
    {
        _helpActive = !_helpActive;
        _help.Text = _helpActive ? "Close Help" : "Help";
        _help.CustomMinimumSize = new Vector2(_helpActive ? 88 : 58, 24);
        _helpSpotlight.Visible = _helpActive;
        if (_helpActive)
        {
            _panel.Visible = false;
            HideWorkGuide();
            _characterPresenter?.Dismiss();
            RefreshContextHelp();
        }
        else
        {
            _helpSpotlight.ClearTarget();
            _helpPopup.Visible = false;
            RefreshHint();
        }
    }

    private void RefreshContextHelp()
    {
        Control? hovered = GetViewport().GuiGetHoveredControl();
        if (!TryResolveHelp(hovered, out Control? target, out HelpDefinition definition))
        {
            _helpSpotlight.ClearTarget();
            _helpPopup.Visible = false;
            return;
        }

        Rect2 rect = target!.GetGlobalRect().Intersection(GetViewport().GetVisibleRect());
        if (rect.Size.X <= 1 || rect.Size.Y <= 1)
        {
            _helpSpotlight.ClearTarget();
            _helpPopup.Visible = false;
            return;
        }

        _helpSpotlight.SetTarget(rect);
        _helpTitle.Text = definition.Title;
        _helpBody.Text = definition.Body;
        PositionHelpPopup(rect);
        _helpPopup.Visible = true;
    }

    private bool TryResolveHelp(Control? hovered, out Control? target, out HelpDefinition definition)
    {
        target = null;
        definition = default;
        if (!GodotObject.IsInstanceValid(hovered) || hovered == _help || hovered == _helpPopup ||
            _helpPopup.IsAncestorOf(hovered))
        {
            return false;
        }

        Control? fallback = null;
        string? fallbackTooltip = null;
        for (Control? current = hovered; GodotObject.IsInstanceValid(current); current = current!.GetParent() as Control)
        {
            if (ExplicitHelp.TryGetValue(current!.Name, out definition))
            {
                target = current;
                return true;
            }
            if (fallback is null && !string.IsNullOrWhiteSpace(current.TooltipText))
            {
                fallback = current;
                fallbackTooltip = current.TooltipText.Trim();
            }
        }

        if (fallback is null || string.IsNullOrWhiteSpace(fallbackTooltip))
            return false;
        target = fallback;
        definition = new HelpDefinition(FriendlyControlName(fallback), fallbackTooltip);
        return true;
    }

    private static string FriendlyControlName(Control control)
    {
        if (control is Button button && !string.IsNullOrWhiteSpace(button.Text))
            return button.Text.Replace("▸", string.Empty).Trim();
        if (control is Label label && !string.IsNullOrWhiteSpace(label.Text))
            return label.Text.Length <= 36 ? label.Text : "Help";
        string name = control.Name;
        return string.IsNullOrWhiteSpace(name) ? "Help" : name;
    }

    private void PositionHelpPopup(Rect2 target)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        const float gap = 10;
        const float width = 340;
        const float height = 150;
        float x = target.End.X + gap;
        if (x + width > viewport.X)
            x = Math.Max(gap, target.Position.X - width - gap);
        float y = Math.Clamp(target.Position.Y, gap, Math.Max(gap, viewport.Y - height - gap));
        _helpPopup.Position = new Vector2(x, y);
        _helpPopup.Size = new Vector2(width, height);
    }

    private void RequestImmediateFlush() => _ = FlushObservedAsync();

    private async Task FlushObservedAsync()
    {
        try
        {
            await _context.Saves.FlushProgressAsync(force: true);
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Tutorial progress save failed; state remains dirty: {exception.Message}");
        }
    }

    private readonly record struct HelpDefinition(string Title, string Body);

    private sealed partial class HelpSpotlightOverlay : Control
    {
        private Rect2? _target;
        private static readonly Color Dim = new(0, 0, 0, 0.58f);

        public void SetTarget(Rect2 rect)
        {
            _target = rect.Grow(3);
            QueueRedraw();
        }

        public void ClearTarget()
        {
            _target = null;
            QueueRedraw();
        }

        public override void _Draw()
        {
            Rect2 viewport = new(Vector2.Zero, Size);
            if (_target is not Rect2 target)
            {
                DrawRect(viewport, Dim, true);
                return;
            }

            target = target.Intersection(viewport);
            DrawRect(new Rect2(0, 0, Size.X, Math.Max(0, target.Position.Y)), Dim, true);
            DrawRect(new Rect2(0, target.End.Y, Size.X, Math.Max(0, Size.Y - target.End.Y)), Dim, true);
            DrawRect(new Rect2(0, target.Position.Y, Math.Max(0, target.Position.X), target.Size.Y), Dim, true);
            DrawRect(new Rect2(target.End.X, target.Position.Y, Math.Max(0, Size.X - target.End.X), target.Size.Y), Dim, true);
            DrawRect(target, Win98ThemeFactory.Highlight, false, 3);
        }
    }
}
