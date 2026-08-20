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
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
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
    private const int TutorialTextWidth = 340;
    private const int TutorialGuideWidth = 124;
    private const int TutorialWidth = TutorialTextWidth + TutorialGuideWidth;
    private const int TutorialHeight = 220;
    private const int WorkGuideWidth = 380;
    private const int WorkGuideHeight = 190;

    /// <summary>Buddy Studio mints category IDs as the lower-cased slot name.</summary>
    private const string StudioNoseCategoryId = "nose";

    /// <summary>
    /// Which subtree a spotlight name is resolved inside. This is not decoration: the character
    /// editor and the background editor both own a control named <c>PaintBrushButton</c>, so a
    /// tree-wide search silently rings whichever one happens to come first.
    /// </summary>
    private enum SpotlightScope { Shell, PaintBuddy, Background, Studio }

    private readonly record struct SpotlightTarget(SpotlightScope Scope, string NodeName);

    /// <summary>
    /// Controls the tutorial spotlight points at, by step. Steps that teach a world action
    /// (grabbing, swinging) or that live in the separate Work window are deliberately absent.
    /// </summary>
    private static readonly Dictionary<string, SpotlightTarget> StepSpotlights =
        new(StringComparer.Ordinal)
        {
            [TutorialStepIds.OpenInventory] = new(SpotlightScope.Shell, "Win98ShopCommand"),
            [TutorialStepIds.OpenPaintBuddy] = new(SpotlightScope.Shell, "Win98PaintCommand"),
            [TutorialStepIds.SelectPaintBrush] = new(SpotlightScope.PaintBuddy, "PaintBrushButton"),
            [TutorialStepIds.SelectPaintColor] = new(SpotlightScope.PaintBuddy, "PaintPresetPalette"),
            [TutorialStepIds.PaintBuddy] = new(SpotlightScope.PaintBuddy, "Win98PaintViewportFrame"),
            [TutorialStepIds.OpenPaintBackground] = new(SpotlightScope.Shell, "Win98PaintCommand"),
            [TutorialStepIds.SelectBackgroundSpray] = new(SpotlightScope.Background, "PaintSprayButton"),
            [TutorialStepIds.SelectBackgroundColor] = new(SpotlightScope.Background, "PaintSwatches"),
            [TutorialStepIds.PaintBackground] = new(SpotlightScope.Background, "EnvironmentBackgroundInputBlocker"),
            [TutorialStepIds.SaveAndExitPaintBackground] = new(SpotlightScope.Background, "PaintSaveButton"),
            // Name is minted from TopLevelCommandIds.BuddyStudio ("command.buddy_studio").
            [TutorialStepIds.OpenBuddyStudio] = new(SpotlightScope.Shell, "TopLevelCommand_command_buddy_studio"),
            [TutorialStepIds.SelectNoseCategory] = new(SpotlightScope.Studio, "BuddyStudioCategories"),
            [TutorialStepIds.SelectNoseButtonStyle] = new(SpotlightScope.Studio, "BuddyStudioCatalog"),
            [TutorialStepIds.BuyStudioItem] = new(SpotlightScope.Studio, "BuddyStudioBuy"),
            [TutorialStepIds.EquipStudioItem] = new(SpotlightScope.Studio, "BuddyStudioBuy"),
            [TutorialStepIds.ExitBuddyStudio] = new(SpotlightScope.Studio, "BuddyStudioCancel"),
            [TutorialStepIds.EnterWorkMode] = new(SpotlightScope.Shell, "Win98WorkCommand"),
        };

    private static readonly Dictionary<string, HelpDefinition> ExplicitHelp =
        new(StringComparer.Ordinal)
        {
            ["Win98CommandBar"] = new("Top bar", "Use this bar to open Inventory, Tools, Paint, Buddy Studio, Work and the other main areas."),
            ["Win98BalanceLabel"] = new("Credits", "Shows your current money count. Earn them by playing with your Buddy or using Work Mode. You can spend them on tools and customisation. Rough play pays the most."),
            ["Win98ShopCommand"] = new("Inventory", "Buy new tools and toys here. Anything you buy is equipped straight away."),
            ["Win98ToolsCommand"] = new("Tools", "Equip any tool you already own."),
            ["Win98PaintCommand"] = new("Paint", "Open the workshops for painting Buddy or the room background."),
            ["Win98WorkCommand"] = new("Work", "Enter Work Mode, where Buddy becomes a small always-on-top companion and keeps earning while you work."),
            ["ContextHelpButton"] = new("Help", "Select ? to turn Help mode on, then hover over anything on screen to learn what it does. Select ? again to leave."),

            ["Win98StatusBar"] = new("Status bar", "The left side shows some extra details when needed. The right side shows your equipped tool."),
            ["StatusText"] = new("Status message", "Shows the latest message from the game, including purchases, saves and other confirmations."),
            ["ActiveToolStatusText"] = new("Equipped tool", "Shows the tool currently on your cursor. Equip a different one from Inventory or Tools."),

            ["Win98CharacterColumn"] = new("Characters", "Choose the Buddy you want to edit."),
            ["Win98PaintLayerPanel"] = new("Layers", "Choose or hide which body-part layer becomes paintable. Hidden layers cannot be painted on and will reappear when you leave the editor."),
            ["Win98PaintToolColumn"] = new("Paint tools", "Choose the Brush or Eraser, change its size, rotate the preview, Undo or Redo, and adjust the view."),
            ["Win98PaintViewportFrame"] = new("Paint canvas", "Paint directly onto Buddy here. The brush follows the surface and only affects the selected layer."),
            ["CharacterPaintCanvas"] = new("Paint canvas", "Click and drag to paint Buddy. The selected tool, colour, size and layer control the result."),
            ["Win98PaintColorFooter"] = new("Colours and actions", "Choose a paint colour here. Save keeps your changes, Use Character applies them in Play Mode, and Exit leaves the editor."),
            ["PaintPresetPalette"] = new("Palette", "Select a swatch to use a saved colour, or open the colour wheel for the full picker."),
            ["PaintPrimaryActions"] = new("Character actions", "Save keeps your changes. Use Character applies them in Play Mode. Reset restores the saved version, and Exit leaves Paint Buddy."),

            ["PaintBackgroundPanel"] = new("Paint Background", "Paint the room backdrop here. Select Save and Exit when you want to keep the result."),
            ["PaintToolGrid"] = new("Background tools", "Choose Brush, Pen, Spray, Fill, Eraser, Pick Colour, a shape tool or Undo."),
            ["PaintBrushSizeRow"] = new("Brush size", "Changes the size of the active background brush. You can also use your scroll wheel."),
            ["PaintBackgroundPalettePanel"] = new("Background palette", "Choose the active colour, add a custom swatch or open the full colour picker. You can also delete them by pressing the 'delete' button."),
            ["EnvironmentBackgroundInputBlocker"] = new("Background canvas", "Paint directly onto the visible room. The tool panel hides while you drag so it does not cover your work."),

            ["BuddyStudioCategories"] = new("Categories", "Choose the part of Buddy you want to customise, such as his eyes, glasses, headwear, top or shoes."),
            ["BuddyStudioPreviewPane"] = new("Preview", "Preview the selected style here. Bought items can be moved or resized in this preview pane before you save."),
            ["BuddyStudioCatalogPane"] = new("Styles", "Select an item once to preview it for free. Owned items can be equipped while unowned styles show their price."),
            ["BuddyStudioInspectorPane"] = new("Colour and ownership", "Change available colours and check whether the selected style is owned, equipped or available to buy."),
            ["BuddyStudioBuy"] = new("Buy / Equip", "Buy the selected style permanently, or equip it if you already own it."),
            ["BuddyStudioActions"] = new("Studio actions", "Save applies your character changes. Exit leaves Buddy Studio and warns you if anything is unsaved."),

            ["WorkCompanionRoot"] = new("Work companion", "Click and hold Buddy or the computer with the left mouse button, then drag the companion anywhere. Double-click Buddy to return to Play Mode."),
            ["WorkControlCluster"] = new("Companion controls", "Use these controls to resize, pause or exit."),
            ["WorkCrtCounter"] = new("Work counter", "Shows how much work you have done. The number increments per action, keyboard press or mouse click. Click on the screen to switch between this session count and your lifetime total count."),
            ["WorkResizeButton"] = new("Resize", "Click and hold this button with the left mouse button, then drag to resize the Work companion. The controls themselves stay the same size."),
            ["WorkMotionToggle"] = new("Motion", "Pauses or resumes Buddy's Work animations. Counters and rewards continue either way."),
            ["WorkExitButton"] = new("Exit Work Mode", "Return to Play Mode. You can also double-click Buddy."),
        };

    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private TutorialProgressState _tutorial = null!;
    private ITutorialCharacterPresenter? _characterPresenter;

    /// <summary>What the prompt is currently showing, so a step whose text depends on live
    /// state can be re-rendered without churning the whole panel every frame.</summary>
    private string? _lastRenderedText;

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Label _body = null!;
    private Button _dismiss = null!;
    private Button _skip = null!;
    private Button _help = null!;
    private bool _helpDocked;
    private bool _panelPlaced;
    private bool _panelInLowerLeft;
    private bool _panelUserMoved;
    private bool _panelDragging;
    private Vector2 _panelDragOffset;

    /// <summary>The only clickable region while a prompt points somewhere; empty means no lock.</summary>
    private Rect2 _lockedTargetRect;
    private Rect2 _lockedAlternateRect;

    /// <summary>Set while the highlighted control lives in another window: the shell is dimmed
    /// whole and accepts nothing, so the player's only live surface is that other window.</summary>
    private bool _lockMainViewport;
    private HelpSpotlightOverlay? _activeForeignSpotlight;
    private readonly Dictionary<ulong, HelpSpotlightOverlay> _foreignSpotlights = new();
    private readonly HashSet<string> _unresolvedSpotlights = new(StringComparer.Ordinal);

    /// <summary>Host for the tutorial guide art, inside the single tutorial window.</summary>
    public Control GuideSlot { get; private set; } = null!;

    private Button _exitHelp = null!;
    private HelpSpotlightOverlay _tutorialSpotlight = null!;
    private HelpSpotlightOverlay _helpSpotlight = null!;

    // Work Mode hides the whole shell, so Help gets a second surface inside the companion's own
    // window: its own dim, popup and exit button, driven by the same region metadata.
    private CanvasLayer? _workHelpLayer;
    private Control? _workHelpRoot;
    private HelpSpotlightOverlay? _workHelpSpotlight;
    private PanelContainer? _workHelpPopup;
    private Label? _workHelpTitle;
    private Label? _workHelpBody;
    private Button? _workHelpToggle;
    private PanelContainer _helpPopup = null!;
    private Label _helpTitle = null!;
    private Label _helpBody = null!;
    private bool _helpActive;

    private Window? _workGuideWindow;
    private Label? _workGuideBody;

    private CharacterEditorHost? _editor;
    private ShopPanel? _shop;
    private Button? _baseballBatAction;
    private WorkCompanionCoordinator? _work;
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    private BuddyStudioWorkspace? _studio;

    private bool _editorSignalsBound;
    private bool _brushSignalBound;
    private bool _studioSignalsBound;
    private bool _backgroundSignalsBound;
    private bool _wasGrabbing;
    private bool _wasEditorOpen;
    private bool _wasStudioOpen;
    private bool _hasSeenWorkActive;
    private bool _paintSaveRequested;
    private bool _paintUseRequested;
    private bool _backgroundSaveRequested;
    private bool _studioSaveRequested;
    private bool _baseballBatActionObserved;
    private bool _chargedBatSwingObserved;
    private bool _swingReleaseSignalBound;
    private bool _hasGrabbedBuddy;
    private long? _torsoRevisionOrigin;

    /// <summary>The document on show when the create step began; a different one means the
    /// player actually pressed New rather than inheriting whatever was already loaded.</summary>
    private Guid? _createOriginCharacterId;
    private bool _hasCreateOrigin;
    private bool _characterListClicked;
    private bool _characterListBound;

    /// <summary>
    /// Whether Buddy was already wearing the Button nose when this Studio visit began. Sampled
    /// on entry and not re-read: it decides both whether the save step can be satisfied by
    /// there being nothing to save, and whether the Exit prompt remarks on it.
    /// </summary>
    private bool _studioNothingToSave;
    private PaintColor? _paintColorOrigin;
    private bool _brushButtonPressed;
    private EnvironmentColor? _backgroundColorOrigin;
    private Rect2I _workDragOrigin;
    private Rect2I _workResizeOrigin;
    private WorkCompanionView? _workView;
    private bool? _workCounterOrigin;

    private string? _displayedStepId;

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
        TryDockHelpButton();
        EnsureWorkHelpSurface();
        AdvanceCurrentStep();

        string? next = _tutorial.NextIncompleteStepId;
        if (!string.Equals(next, _displayedStepId, StringComparison.Ordinal))
        {
            RefreshHint();
        }
        else if (next is not null && !string.Equals(TextFor(next), _lastRenderedText, StringComparison.Ordinal))
        {
            // Two prompts read live state — whether a character slot is free, and whether the
            // Studio save step found anything to save — and the slot bootstrap settles a frame
            // or two after the editor opens. Rendering once when the step opened therefore
            // showed the wrong half: a player with every slot full was still told to press
            // "+ New Character", which was greyed out (owner report 2026-08-20).
            RefreshHint();
        }

        if (_helpActive)
            RefreshContextHelp();
        RefreshTutorialSpotlight();
        RefreshPaintMenuGate();
        PositionPanelForStep();

        _wasEditorOpen = GodotObject.IsInstanceValid(_editor) && _editor!.IsEditorOpen;
        _wasStudioOpen = IsStudioOpen();
    }

    public override void _Input(InputEvent input)
    {
        // Escape is the reflex for "get me out of this mode"; honour it before anything else.
        if (_helpActive && input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            ExitContextHelp();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (input is not InputEventMouseButton mouse)
            return;

        // Help mode is observational: hovering is allowed, clicking underlying gameplay/UI is not.
        // The Help button itself remains clickable so the mode can always be closed.
        if (_helpActive)
        {
            // Everything that can leave Help mode stays clickable, or the mode traps the player.
            if (GodotObject.IsInstanceValid(_help) && _help.GetGlobalRect().HasPoint(mouse.Position))
                return;
            if (_exitHelp.Visible && _exitHelp.GetGlobalRect().HasPoint(mouse.Position))
                return;
            if (GodotObject.IsInstanceValid(_workHelpToggle) &&
                _workHelpToggle!.GetGlobalRect().HasPoint(mouse.Position))
            {
                return;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Nothing below this line applies once the walkthrough is finished or skipped: the lock
        // exists to keep a prompt honest, and a game with no prompt must accept every click.
        if (_displayedStepId is null || !_panel.Visible)
            return;

        // While a prompt points at one control, that control and the tutorial window are the only
        // clickable things. Steps with no on-screen target (grabbing, swinging, painting the
        // canvas) lock nothing, and Skip Tutorial is always reachable inside the window.
        if (!_lockMainViewport)
        {
            // Steps whose action is out in the world (grab, swing, drop) highlight nothing, so
            // they cannot lock to a rectangle — but the top bar must still be off limits, or the
            // player can wander into Paint or Work in the middle of learning to swing a bat.
            if (!_lockedTargetRect.HasArea())
            {
                if (!IsOverCommandBar(mouse.Position))
                    return;
            }
            else
            {
                if (_lockedTargetRect.HasPoint(mouse.Position))
                    return;
                if (_lockedAlternateRect.HasArea() && _lockedAlternateRect.HasPoint(mouse.Position))
                    return;
            }
        }
        if (_panel.Visible && _panel.GetGlobalRect().HasPoint(mouse.Position))
            return;
        if (GodotObject.IsInstanceValid(_help) && _help.GetGlobalRect().HasPoint(mouse.Position))
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        UnbindActionSignals();
        _characterPresenter?.Dismiss();
        if (GodotObject.IsInstanceValid(_workGuideWindow))
            _workGuideWindow!.QueueFree();
    }

    /// <summary>
    /// Replay the walkthrough from Grab Buddy. Clearing the durable record is the whole job:
    /// the controller already re-derives the prompt, spotlight and lock from it every frame.
    /// </summary>
    public void RestartTutorial()
    {
        _tutorial.Restart();
        _sandbox.Pipeline.SelectTool(ToolId.Grab);
        _hasGrabbedBuddy = false;
        _wasGrabbing = false;
        _baseballBatActionObserved = false;
        _chargedBatSwingObserved = false;
        _torsoRevisionOrigin = null;
        _hasSeenWorkActive = false;
        _workCounterOrigin = null;
        RequestImmediateFlush();
        RefreshHint();
    }

    public bool SkipTutorial()
    {
        bool changed = _tutorial.Skip();
        if (changed)
            RequestImmediateFlush();
        RefreshHint();
        return changed;
    }

    private void DiscoverRuntimeNodes()
    {
        _editor ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        // Not ??=: the Work companion is destroyed on exit and rebuilt on the next entry, and a
        // freed Godot object is invalid but not null. The stale reference then failed every
        // IsInstanceValid check, so the counter step could never complete and the walkthrough
        // stopped dead on the second visit to Work Mode (owner report 2026-08-20).
        if (!GodotObject.IsInstanceValid(_workView))
        {
            var rediscovered = GetTree().Root.FindChild(nameof(WorkCompanionView), true, false) as WorkCompanionView;
            if (!ReferenceEquals(rediscovered, _workView))
            {
                _workView = rediscovered;
                // A fresh companion starts the counter lesson over: the baseline belonged to the
                // instance that just went away.
                _workCounterOrigin = null;
                _workHelpLayer = null;
            }
        }
        _shop ??= GetTree().Root.FindChild("ShopPanel", true, false) as ShopPanel;
        _work ??= GetTree().Root.FindChild(nameof(WorkCompanionCoordinator), true, false) as WorkCompanionCoordinator;
        _backgroundEditor ??= GetTree().Root.FindChild(nameof(EnvironmentBackgroundEditor), true, false) as EnvironmentBackgroundEditor;
        _backgroundPresenter ??= GetTree().Root.FindChild(nameof(EnvironmentBackgroundPresenter), true, false) as EnvironmentBackgroundPresenter;
        _studio ??= GetTree().Root.FindChild(nameof(BuddyStudioWorkspace), true, false) as BuddyStudioWorkspace;
    }

    private void BindActionSignals()
    {
        if (!GodotObject.IsInstanceValid(_baseballBatAction) &&
            GodotObject.IsInstanceValid(_shop) &&
            _shop!.BuyButtonFor(ContentIds.ToolBaseballBat) is Button baseballBatAction)
        {
            _baseballBatAction = baseballBatAction;
            _baseballBatAction.Pressed += OnBaseballBatActionPressed;
        }

        if (!_swingReleaseSignalBound && GodotObject.IsInstanceValid(_sandbox.CursorTools))
        {
            _sandbox.CursorTools.SwingReleased += OnSwingReleased;
            _swingReleaseSignalBound = true;
        }

        if (!_editorSignalsBound && GodotObject.IsInstanceValid(_editor) && _editor!.IsInitialized)
        {
            _editor.SaveButton.Pressed += OnPaintSavePressed;
            _editor.UseButton.Pressed += OnPaintUsePressed;
            _editorSignalsBound = true;
        }

        if (!_brushSignalBound && GodotObject.IsInstanceValid(_editor) &&
            _editor!.FindChild("PaintBrushButton", true, false) is Button brush)
        {
            brush.Pressed += OnPaintBrushPressed;
            _brushSignalBound = true;
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
        if (GodotObject.IsInstanceValid(_baseballBatAction))
            _baseballBatAction!.Pressed -= OnBaseballBatActionPressed;

        if (_swingReleaseSignalBound && GodotObject.IsInstanceValid(_sandbox?.CursorTools))
            _sandbox!.CursorTools.SwingReleased -= OnSwingReleased;

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

    /// <summary>
    /// A click on the library counts as choosing a character even when it re-picks the one
    /// already loaded, which changes no id. Bound lazily: the list is built with the editor.
    /// </summary>
    private void BindCharacterListSignal()
    {
        if (_characterListBound || FindInEditor("CharacterLibraryList") is not ItemList list)
            return;

        list.ItemSelected += _ => _characterListClicked = true;
        _characterListBound = true;
    }

    private void AdvanceCurrentStep()
    {
        BindCharacterListSignal();
        string? step = _tutorial.NextIncompleteStepId;
        if (step is null)
            return;

        bool grabbing = _sandbox.Grab.IsGrabbing;
        if (grabbing && _sandbox.Grab.CurrentGrab.Target is PuppetPartBody)
            _hasGrabbedBuddy = true;
        // Complete on let-go, not on pick-up: advancing mid-hold spotlighted the next step
        // while the player was still dragging Buddy around.
        bool releasedBuddy = !grabbing && _wasGrabbing && _hasGrabbedBuddy;
        _wasGrabbing = grabbing;

        switch (step)
        {
            case TutorialStepIds.GrabBuddy when releasedBuddy:
                GrantFirstCredit();
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenInventory when GodotObject.IsInstanceValid(_shop) && _shop!.IsVisibleInTree():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.PurchaseBaseballBat when
                _baseballBatActionObserved &&
                _context.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat) &&
                _sandbox.Pipeline.SelectedTool == ToolId.BaseballBat:
                _baseballBatActionObserved = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.ChargedBatHit when _chargedBatSwingObserved:
                _chargedBatSwingObserved = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.UnequipTool when _sandbox.Pipeline.SelectedTool != ToolId.BaseballBat:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenPaintBuddy when IsPaintBuddyOpen():
                _paintSaveRequested = false;
                _paintUseRequested = false;
                _hasCreateOrigin = false;
                _characterListClicked = false;
                _torsoRevisionOrigin = null;
                _paintColorOrigin = null;
                _brushButtonPressed = false;
                CompleteCurrent(step);
                break;

            // Creating already asks for the name in the same dialog, so this is one lesson, not
            // two. Picking an existing character from the list satisfies it as well: a player
            // who is out of slots, or replaying, has nothing to create.
            case TutorialStepIds.CreateBuddy when HasChosenCharacter():
                _characterListClicked = false;
                CompleteCurrent(step);
                break;

            // Brush is already the default tool, so state alone would complete this instantly and
            // the player would never see the lesson. Require the actual button press.
            case TutorialStepIds.SelectPaintBrush when IsPaintBuddyOpen() && _brushButtonPressed &&
                                                         _editor!.PaintWorkspace.SelectedTool == PaintTool.Brush:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SelectPaintColor when HasChosenPaintColor():
                CompleteCurrent(step);
                break;

            // Same let-go rule the grab step follows: the first dab already bumps the surface
            // revision, so completing on the press spotlighted the next step while the player
            // was still dragging the brush. Wait for the button.
            // Paint and save are one step. The let-go rule the paint half used to need is gone
            // with it: the save click is the gate now, so there is no way to advance mid-stroke.
            case TutorialStepIds.PaintBuddy when IsPaintBuddyOpen() && HasPaintedTorso() &&
                                                   _paintSaveRequested && !_editor!.Session.IsDirty:
                _paintSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.UsePaintedBuddy when _paintUseRequested && _wasEditorOpen &&
                                                        GodotObject.IsInstanceValid(_editor) && !_editor!.IsEditorOpen:
                _paintUseRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenPaintBackground when IsBackgroundOpen():
                _backgroundSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SelectBackgroundSpray when IsBackgroundOpen() &&
                                                              _backgroundPresenter!.Canvas.Tool == EnvironmentPaintTool.Spray:
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SelectBackgroundColor when HasChosenBackgroundColor():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.PaintBackground when IsBackgroundOpen() &&
                                                        _backgroundPresenter!.Canvas.IsDirty && !IsPrimaryMouseHeld():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.FloatPaintBackgroundPanel when IsBackgroundOpen() && IsBackgroundPanelFloating():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SaveAndExitPaintBackground when _backgroundSaveRequested &&
                                                                   GodotObject.IsInstanceValid(_backgroundEditor) && !_backgroundEditor!.IsOpen &&
                                                                   GodotObject.IsInstanceValid(_backgroundPresenter) && !_backgroundPresenter!.Canvas.IsDirty:
                _backgroundSaveRequested = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.OpenBuddyStudio when IsStudioOpen():
                _studioSaveRequested = false;
                // Decided once, on entry. Asking again at the save step made the "he already had
                // that nose" remark fire for a player who had just equipped it a moment earlier
                // (owner report 2026-08-20).
                _studioNothingToSave = IsStudioEquipped(CharacterFeatureIds.NoseButton);
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SelectNoseCategory when IsStudioOpen() &&
                                                           _studio!.SelectedSlot == CharacterFeatureSlot.Nose:
                CompleteCurrent(step);
                break;

            // Catalogue tiles and the document are keyed by feature ID ("nose.button"), while
            // ownership is keyed by content ID ("cosmetic.nose.button"). Mixing them up is what
            // left the preview step stuck with no way forward.
            case TutorialStepIds.SelectNoseButtonStyle when IsStudioPreviewing(CharacterFeatureIds.NoseButton):
                CompleteCurrent(step);
                break;

            case TutorialStepIds.BuyStudioItem when _context.Progress.IsToolUnlocked(ContentIds.CosmeticNoseButton):
                CompleteCurrent(step);
                break;

            case TutorialStepIds.EquipStudioItem when IsStudioEquipped(CharacterFeatureIds.NoseButton):
                CompleteCurrent(step);
                break;

            // A buddy who already wore the nose on entry leaves nothing to save, so Save is
            // disabled and the walkthrough used to stop dead here. Nothing to save is a
            // completed save; the Exit prompt explains why no button was pressed. Gated on the
            // entry snapshot, so equipping the nose during this visit takes the normal path.
            case TutorialStepIds.SaveBuddyStudio when IsStudioOpen() && _studioNothingToSave &&
                                                        StudioHasNothingToSave():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.SaveBuddyStudio when IsStudioOpen() && _studioSaveRequested && !_editor!.Session.IsDirty:
                _studioSaveRequested = false;
                _studioNothingToSave = false;
                CompleteCurrent(step);
                break;

            case TutorialStepIds.ExitBuddyStudio when _wasStudioOpen && !IsStudioOpen():
                CompleteCurrent(step);
                break;

            case TutorialStepIds.EnterWorkMode when IsWorkActive():
                _hasSeenWorkActive = true;
                _workDragOrigin = _sandbox.Window.WorkCompanionRect;
                _workCounterOrigin = null;
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

            case TutorialStepIds.ToggleWorkCounter when HasSwitchedWorkCounter():
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

    private bool IsBackgroundOpen() =>
        GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen &&
        GodotObject.IsInstanceValid(_backgroundPresenter);

    private bool IsBackgroundPanelFloating() =>
        GetTree().Root.FindChild("PaintBackgroundPinController", true, false) is Win98PinnablePanel pin &&
        pin.IsFloating;

    /// <summary>Any colour will do — the lesson is the palette, not a particular hue.</summary>
    private bool HasChosenPaintColor()
    {
        if (!IsPaintBuddyOpen())
            return false;
        PaintColor colour = _editor!.PaintWorkspace.SelectedColor;
        if (_paintColorOrigin is not PaintColor origin)
        {
            _paintColorOrigin = colour;
            return false;
        }
        return colour != origin;
    }

    /// <summary>Any colour will do here — the lesson is the palette, not a particular hue.</summary>
    private bool HasChosenBackgroundColor()
    {
        if (!IsBackgroundOpen())
            return false;
        EnvironmentColor colour = _backgroundPresenter!.Canvas.Color;
        if (_backgroundColorOrigin is not EnvironmentColor origin)
        {
            _backgroundColorOrigin = colour;
            return false;
        }
        return colour != origin;
    }

    /// <summary>
    /// The torso surface bumps its revision on any accepted stroke, so the tutorial can require
    /// paint <em>on the torso</em> without cloning a megabyte of pixels every frame.
    /// </summary>
    private bool HasPaintedTorso()
    {
        if (!IsPaintBuddyOpen() ||
            !_editor!.PaintWorkspace.Surfaces.TryGetValue(PaintPart.Torso, out PaintSurface? torso))
        {
            return false;
        }
        if (_torsoRevisionOrigin is not long origin)
        {
            _torsoRevisionOrigin = torso.Revision;
            return false;
        }
        return torso.Revision > origin;
    }

    /// <summary>
    /// True while the player is still holding the paint stroke down. Read from the device
    /// rather than a per-canvas flag so the Buddy and Background editors — which share no
    /// stroke state — obey the same rule; headless scenarios drive the model directly and
    /// always read false.
    /// </summary>
    private static bool IsPrimaryMouseHeld() => Input.IsMouseButtonPressed(MouseButton.Left);

    /// <summary>
    /// True once the player has settled on a character to paint — a new one, or a deliberate
    /// pick from the library. The id showing when the step opened is the baseline, so whatever
    /// happened to be loaded does not satisfy the lesson on its own; but a click on the list
    /// does, because re-picking the character you already had is a real choice and changes no id.
    /// </summary>
    private bool HasChosenCharacter()
    {
        if (!IsPaintBuddyOpen())
            return false;

        Guid? current = _editor!.Session.SelectedCharacterId;
        if (!_hasCreateOrigin)
        {
            _createOriginCharacterId = current;
            _hasCreateOrigin = true;
            return false;
        }

        if (_characterListClicked && current is not null)
            return true;

        return current is not null && current != _createOriginCharacterId;
    }

    /// <summary>
    /// The Studio save step has nothing to do: the character is not dirty and Save is disabled.
    /// Both are checked because "not dirty" alone is momentarily true while the workspace is
    /// still settling after the equip.
    /// </summary>
    private bool StudioHasNothingToSave() =>
        GodotObject.IsInstanceValid(_studio) &&
        GodotObject.IsInstanceValid(_studio!.SaveAction) &&
        _studio.SaveAction.Disabled &&
        GodotObject.IsInstanceValid(_editor) &&
        !_editor!.Session.IsDirty;

    private Control? FindInEditor(string nodeName) =>
        GodotObject.IsInstanceValid(_editor)
            ? _editor!.FindChild(nodeName, true, false) as Control
            : null;

    /// <summary>
    /// Whether a fresh slot is still available. Read off the button the slot bootstrap already
    /// disables when the player is full, rather than recomputing the entitlement maths here.
    /// </summary>
    private bool CanCreateCharacter() =>
        FindInEditor("Win98NewCharacterButton") is Button create && !create.Disabled;

    private bool IsStudioPreviewing(string contentId) =>
        IsStudioOpen() && GodotObject.IsInstanceValid(_studio!.CatalogGrid) &&
        string.Equals(_studio.CatalogGrid.SelectedId, contentId, StringComparison.Ordinal);

    private bool IsStudioEquipped(string contentId) =>
        IsStudioOpen() && GodotObject.IsInstanceValid(_editor) &&
        _editor!.Session.WorkingDocument is CharacterDocument document &&
        string.Equals(
            CharacterDocumentEditor.ReadFeatureId(document, CharacterFeatureSlot.Nose),
            contentId,
            StringComparison.Ordinal);

    /// <summary>
    /// True once the player has flipped the Work CRT between session and lifetime totals. The
    /// baseline is captured on the first frame the counter is observable rather than at Work
    /// entry, because the view is built asynchronously with the companion window.
    /// </summary>
    private bool HasSwitchedWorkCounter()
    {
        if (!IsWorkActive() || !GodotObject.IsInstanceValid(_workView))
            return false;
        bool showLifetime = _workView!.ShowLifetime;
        if (_workCounterOrigin is not bool origin)
        {
            _workCounterOrigin = showLifetime;
            return false;
        }
        return showLifetime != origin;
    }

    private void OnSwingReleased(float releasedCharge, int swingEpoch)
    {
        _ = releasedCharge;
        if (swingEpoch > 0 && _sandbox.CursorTools.ActiveContentId == ContentIds.ToolBaseballBat)
            _chargedBatSwingObserved = true;
    }

    private void OnBaseballBatActionPressed()
    {
        if (_tutorial.NextIncompleteStepId == TutorialStepIds.PurchaseBaseballBat)
            _baseballBatActionObserved = true;
    }

    /// <summary>
    /// The very next thing the walkthrough asks for is a 1-credit purchase, so that first handful
    /// of Buddy is worth exactly that. Manhandling a buddy pays fractions of a credit, which
    /// would leave the player staring at a bat they cannot afford. Tops up to one whole credit
    /// rather than adding one, so a player who already earned some is not handed extra.
    /// </summary>
    private void GrantFirstCredit()
    {
        long shortfall = RewardLedger.MilliCreditsPerCredit - _context.Progress.BalanceMilliCredits;
        if (shortfall > 0)
            _context.Progress.Deposit(shortfall);
    }

    private void OnPaintBrushPressed() => _brushButtonPressed = true;
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

        _panel = CreateWin98MessagePanel(
            "FirstSessionGuidancePanel",
            "Desktop Buddy Help",
            out _body,
            out HBoxContainer actions,
            out Control? guideSlot,
            draggable: true);
        GuideSlot = guideSlot!;
        _panel.CustomMinimumSize = new Vector2(TutorialWidth, TutorialHeight);
        _panel.Size = new Vector2(TutorialWidth, TutorialHeight);
        _root.AddChild(_panel);

        // No Dismiss: the walkthrough gates real actions now, so hiding a prompt would only
        // strand the player behind an input lock they cannot see the reason for.
        _dismiss = Win98Dialog.Action(actions, "Continue", AcknowledgeCurrent);
        _dismiss.TooltipText = "Continue the walkthrough.";
        _skip = Win98Dialog.Action(actions, "Skip Tutorial", () => SkipTutorial());
        _skip.TooltipText = "Stop the first-session walkthrough. The Help button remains available.";
    }

    private PanelContainer CreateWin98MessagePanel(
        string name,
        string title,
        out Label bodyLabel,
        out HBoxContainer actions) =>
        CreateWin98MessagePanel(name, title, out bodyLabel, out actions, out _, draggable: false);

    /// <summary>
    /// One Win98 message window. When a guide slot is requested the helper art lives inside this
    /// same frame as a square on the right, instead of trailing the prompt as a second window.
    /// </summary>
    private PanelContainer CreateWin98MessagePanel(
        string name,
        string title,
        out Label bodyLabel,
        out HBoxContainer actions,
        out Control? guideSlot,
        bool draggable)
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
            MouseFilter = draggable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
            MouseDefaultCursorShape = draggable ? Control.CursorShape.Move : Control.CursorShape.Arrow,
        };
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titleBar);
        if (draggable)
            titleBar.GuiInput += OnPanelTitleInput;
        var titleLabel = new Label
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        titleBar.AddChild(titleLabel);

        var split = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        split.AddThemeConstantOverride("separation", 8);
        column.AddChild(split);

        var textColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        textColumn.AddThemeConstantOverride("separation", 6);
        split.AddChild(textColumn);

        bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Pin the wrap width so the panel's minimum height is computed against the width it
            // will actually have, not against whatever width it happens to hold this frame.
            CustomMinimumSize = new Vector2(draggable ? TutorialTextWidth : 0, 0),
        };
        textColumn.AddChild(bodyLabel);

        actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        actions.AddThemeConstantOverride("separation", 6);
        textColumn.AddChild(actions);

        guideSlot = null;
        if (draggable)
        {
            guideSlot = new Control
            {
                Name = "TutorialGuideSlot",
                CustomMinimumSize = new Vector2(TutorialGuideWidth, 0),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            split.AddChild(guideSlot);
        }
        return panel;
    }

    private void OnPanelTitleInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _panelDragging = button.Pressed;
                if (button.Pressed)
                    _panelDragOffset = _panel.GetGlobalRect().Position - button.GlobalPosition;
                break;
            case InputEventMouseMotion motion when _panelDragging:
                Vector2 viewport = GetViewport().GetVisibleRect().Size;
                Vector2 wanted = motion.GlobalPosition + _panelDragOffset;
                _panel.Position = new Vector2(
                    Math.Clamp(wanted.X, 0, Math.Max(0, viewport.X - _panel.Size.X)),
                    Math.Clamp(wanted.Y, 0, Math.Max(0, viewport.Y - _panel.Size.Y)));
                _panelUserMoved = true;
                break;
        }
    }

    /// <summary>
    /// Home is the middle of the right edge. Paint Buddy is the exception: its canvas and action
    /// row live on the right, so the window steps down to the lower left while that workspace is
    /// open. The player can always drag it; a move only happens when the zone actually changes.
    /// </summary>
    private void PositionPanelForStep()
    {
        bool lowerLeft = _displayedStepId is
            TutorialStepIds.OpenPaintBuddy or TutorialStepIds.SelectPaintBrush or
            TutorialStepIds.SelectPaintColor or TutorialStepIds.PaintBuddy or
            TutorialStepIds.PaintBuddy or TutorialStepIds.UsePaintedBuddy;

        // A drag wins until the workspace changes under it, so the window never fights the player.
        if (_panelDragging || (_panelUserMoved && lowerLeft == _panelInLowerLeft))
            return;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
            return;

        // Clamp rather than bail: a viewport briefly smaller than the window during boot used to
        // leave the panel parked at the top-left default forever.
        const float margin = 16;
        Vector2 size = _panel.Size;
        float x = lowerLeft ? margin : viewport.X - size.X - margin;
        float y = lowerLeft ? viewport.Y - size.Y - margin : (viewport.Y - size.Y) * 0.5f;
        _panel.Position = new Vector2(
            Math.Clamp(x, 0, Math.Max(0, viewport.X - size.X)),
            Math.Clamp(y, 0, Math.Max(0, viewport.Y - size.Y)));
        _panelInLowerLeft = lowerLeft;
        _panelUserMoved = false;
        _panelPlaced = true;
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
        Win98Dialog.Action(actions, "Skip Tutorial", () => SkipTutorial());
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

    /// <summary>
    /// Two prompts have nothing in the world to observe — the sign-off and the compliment after
    /// Paint Buddy. Pressing Continue <em>is</em> their action.
    /// </summary>
    /// <summary>
    /// Steps the player dismisses with the button: the two compliments and the sign-off, which
    /// have nothing in the world to observe.
    /// </summary>
    private static bool IsAcknowledgeStep(string? stepId) => stepId is
        TutorialStepIds.AdmirePaintedBuddy or TutorialStepIds.AdmireStudioBuddy or
        TutorialStepIds.Farewell;

    private void AcknowledgeCurrent()
    {
        if (IsAcknowledgeStep(_displayedStepId))
            CompleteCurrent(_displayedStepId!);
    }

    private void RefreshHint()
    {
        string? stepId = _tutorial.NextIncompleteStepId;
        _displayedStepId = stepId;
        if (_helpActive || stepId is null)
        {
            _panel.Visible = false;
            HideWorkGuide();
            _characterPresenter?.Dismiss();
            return;
        }

        string text = TextFor(stepId);
        _lastRenderedText = text;
        bool farewell = string.Equals(stepId, TutorialStepIds.Farewell, StringComparison.Ordinal);
        _dismiss.Visible = IsAcknowledgeStep(stepId);
        _dismiss.Text = farewell ? "Goodbye" : "Continue";
        _skip.Visible = !farewell;

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
        // Prompts vary from four words to four lines; grow rather than clip the longer lessons.
        _panel.Size = new Vector2(
            TutorialWidth,
            Math.Max(TutorialHeight, _panel.GetCombinedMinimumSize().Y));
        _characterPresenter?.Present(stepId, text);
    }

    private void HideWorkGuide()
    {
        if (GodotObject.IsInstanceValid(_workGuideWindow))
            _workGuideWindow!.Visible = false;
    }

    private static bool IsWorkTutorialStep(string stepId) => stepId is
        TutorialStepIds.EnterWorkMode or TutorialStepIds.DragWorkCompanion or
        TutorialStepIds.ResizeWorkCompanion or TutorialStepIds.ToggleWorkCounter or
        TutorialStepIds.ExitWorkMode;

    /// <summary>
    /// Instance, not static: two prompts read live state — whether a character slot is still
    /// free, and whether the Studio save step found anything to save.
    /// </summary>
    private string TextFor(string stepId) => stepId switch
    {
        TutorialStepIds.GrabBuddy =>
            "Hi! Let me introduce you to your buddy. Click and hold your left mouse button on " +
            "your Buddy to grab him.",
        TutorialStepIds.OpenInventory =>
            "Now open the Inventory in the top-left corner. This is where you can buy and equip " +
            "all sorts of different tools so you and your Buddy can play together.",
        TutorialStepIds.PurchaseBaseballBat =>
            "You can use your Credits in the top-right to buy all kinds of things. You earn more " +
            "by playing with your Buddy. For now, let's buy and equip the Baseball Bat.",
        TutorialStepIds.ChargedBatHit =>
            "Nice stuff! You can hold the right mouse button to charge your bat up for a big " +
            "swing. Some other tools have some extra interaction by clicking or holding the " +
            "right mouse button. Try them out yourself later, but for now try hitting the buddy " +
            "with a charged swing.",
        TutorialStepIds.UnequipTool =>
            "To unequip a tool you can switch in the Inventory or press the 'D' button to drop " +
            "it. You can re-equip dropped tools by double-clicking it. Try to drop it now.",
        TutorialStepIds.OpenPaintBuddy =>
            "Your Buddy could use a little colour. Open Paint ▸ Buddy in the menu above to give " +
            "it a new look.",
        TutorialStepIds.CreateBuddy => CanCreateCharacter()
            ? "Does this look familiar? First, let's create a new Buddy for you. Click on " +
              "'+ New Character', give it a name, and you are ready to paint."
            : "Hmm, you've been here before so choose a Buddy from the Characters list instead.",
        TutorialStepIds.SelectPaintBrush =>
            "There are many tools to choose from. Let's start with my favourite which is the " +
            "Brush tool to start painting.",
        TutorialStepIds.SelectPaintColor =>
            "Here you can choose any colour you like. The big button to the right opens up the " +
            "advanced color pallette. Let's just choose one of these for now.",
        TutorialStepIds.PaintBuddy =>
            "Paint away! When you are happy, click on the 'Save' button below.",
        TutorialStepIds.UsePaintedBuddy =>
            "Click on the 'Use Character' to start playing with your new Buddy!",
        TutorialStepIds.AdmirePaintedBuddy =>
            "Beautiful! Buddy has never looked better.",
        TutorialStepIds.OpenPaintBackground =>
            "Your Buddy deserves a better room that matches its new style. Open Paint ▸ " +
            "Background to start painting the room.",
        TutorialStepIds.SelectBackgroundSpray =>
            "Let's go with something new but nostalgic, the Spray tool!",
        TutorialStepIds.SelectBackgroundColor => "Choose a nice matching colour from the Palette.",
        TutorialStepIds.PaintBackground =>
            "Click and drag anywhere on the room behind your Buddy to spray it.",
        TutorialStepIds.FloatPaintBackgroundPanel =>
            "We need to admire your drawing more. Drag any panel by its title bar and move it " +
            "outside of the game window. You can also click the red pin button.",
        TutorialStepIds.SaveAndExitPaintBackground =>
            "Looks good! Click Save and Exit to keep it.",
        TutorialStepIds.OpenBuddyStudio =>
            "Now let's bring your buddy more up to style. Open Buddy Studio so we can customise " +
            "your Buddy with lots of apparel.",
        TutorialStepIds.SelectNoseCategory =>
            "Let's see, your buddy could use a new nose. Click on the Nose category to see what " +
            "we've got.",
        TutorialStepIds.SelectNoseButtonStyle =>
            "This 'Button nose' could be fun! Click on it once to preview it without buying it.",
        TutorialStepIds.BuyStudioItem =>
            "It looks great! Let's click on the 'Buy' button. Alternatively, you can double-click " +
            "on an item to buy it.",
        TutorialStepIds.EquipStudioItem =>
            "Let's equip it for now. You can swap it later, or choose the default style to " +
            "remove it.",
        TutorialStepIds.SaveBuddyStudio =>
            "Click on the 'Save' button to keep this beautiful nose.",
        TutorialStepIds.ExitBuddyStudio => _studioNothingToSave
            ? "Your Buddy was already wearing that nose, so there is no need to save. Let's exit " +
              "to the play screen."
            : "Let's exit to the play screen.",
        TutorialStepIds.AdmireStudioBuddy =>
            "Now that is what I call a nose. Now your buddy is looking mighty fine!",
        TutorialStepIds.EnterWorkMode =>
            "Last but not least: Work Mode. Enter work mode for when you need to concentrate and " +
            "want to let your buddy sit beside you.",
        TutorialStepIds.DragWorkCompanion =>
            "Click and hold on the Buddy with the left mouse button to drag your companion " +
            "wherever you want it.",
        TutorialStepIds.ResizeWorkCompanion =>
            "Click and hold the resize button ↘ with the left mouse button, then drag to make " +
            "your companion bigger or smaller.",
        TutorialStepIds.ToggleWorkCounter =>
            "Your Buddy will earn money while you work. Click on the screen to switch between " +
            "this session's total count and your lifetime total count.",
        TutorialStepIds.ExitWorkMode =>
            "Ready to head back to play mode? Double-click on your Buddy or click on the 'X' " +
            "button to return.",
        TutorialStepIds.Farewell =>
            "Well that was it, I hope you'll become the best of buds! If you ever need help, " +
            "click on the '?' in the title bar and hover over anything on screen for context, " +
            "or restart the tutorial from the settings screen. Have fun with your Buddy!",
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

        // Faint by design: the tutorial points, it does not black the game out.
        _tutorialSpotlight = new HelpSpotlightOverlay
        {
            Name = "TutorialSpotlight",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            DimAlpha = 0.41f,
        };
        _tutorialSpotlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_tutorialSpotlight);

        _helpSpotlight = new HelpSpotlightOverlay
        {
            Name = "ContextHelpSpotlight",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _helpSpotlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_helpSpotlight);

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

        _exitHelp = BuildExitHelpButton();
        _root.AddChild(_exitHelp);

        _root.MoveChild(_help, _root.GetChildCount() - 1);
        // The tutorial window must stay readable above its own dim.
        _root.MoveChild(_panel, _root.GetChildCount() - 1);
    }

    /// <summary>
    /// A plainly labelled way out of Help mode, anchored bottom-right. The `?` in the title bar
    /// toggles too, but it is a small icon in a corner the player may not have looked at, and
    /// Work Mode hides that title bar entirely.
    /// </summary>
    private Button BuildExitHelpButton()
    {
        var button = new Button
        {
            Name = "ExitHelpModeButton",
            Text = "Exit Help Mode",
            TooltipText = "Leave Help mode. Escape does the same.",
            Visible = false,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(132, 26),
            Theme = Win98ThemeFactory.Create(),
        };
        button.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        button.OffsetLeft = -148;
        button.OffsetTop = -42;
        button.OffsetRight = -16;
        button.OffsetBottom = -16;
        button.Pressed += ExitContextHelp;
        return button;
    }

    private void ExitContextHelp()
    {
        if (_helpActive)
            ToggleContextHelp();
    }

    /// <summary>
    /// Compose the Work Mode help surface inside the companion window the first time Work is
    /// entered. It cannot live in the main viewport: that window is hidden while Work is active.
    /// </summary>
    private void EnsureWorkHelpSurface()
    {
        if (_workHelpLayer is not null || !GodotObject.IsInstanceValid(_workView))
            return;
        Window window = _workView!.GetWindow();
        // Before Work Mode is entered the companion view still hangs off the main window, so
        // GetWindow() returns the shell. Building here would drop a second `?` on the shell's
        // own title bar, on top of the close box. Wait for the real companion window.
        if (!GodotObject.IsInstanceValid(window) || window == GetWindow())
            return;

        _workHelpLayer = new CanvasLayer { Name = "WorkContextHelpLayer", Layer = 250 };
        window.AddChild(_workHelpLayer);

        _workHelpRoot = new Control
        {
            Name = "WorkContextHelpRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _workHelpRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _workHelpLayer.AddChild(_workHelpRoot);

        _workHelpSpotlight = new HelpSpotlightOverlay
        {
            Name = "WorkContextHelpSpotlight",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _workHelpSpotlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _workHelpRoot.AddChild(_workHelpSpotlight);

        _workHelpPopup = CreateWin98MessagePanel(
            "WorkContextHelpPopup",
            "Help",
            out Label body,
            out HBoxContainer actions);
        _workHelpBody = body;
        _workHelpPopup.Visible = false;
        _workHelpPopup.CustomMinimumSize = new Vector2(300, 116);
        _workHelpPopup.MouseFilter = Control.MouseFilterEnum.Ignore;
        _workHelpRoot.AddChild(_workHelpPopup);
        actions.Visible = false;
        _workHelpTitle = _workHelpPopup.FindChild("WorkContextHelpPopupTitleBar", true, false)?
            .GetChildOrNull<Label>(0);

        _workHelpToggle = new Button
        {
            Name = "WorkContextHelpButton",
            Text = "?",
            TooltipText = "Explain the part of the Work companion you hover over.",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(26, 22),
            Theme = Win98ThemeFactory.Create(),
        };
        _workHelpToggle.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _workHelpToggle.OffsetLeft = -36;
        _workHelpToggle.OffsetTop = 6;
        _workHelpToggle.OffsetRight = -10;
        _workHelpToggle.OffsetBottom = 28;
        _workHelpToggle.Pressed += ToggleContextHelp;
        _workHelpRoot.AddChild(_workHelpToggle);
    }

    /// <summary>True while Help should be presented inside the Work companion window.</summary>
    private bool UseWorkHelpSurface() => IsWorkActive() && _workHelpRoot is not null;

    /// <summary>
    /// Move the Help command into the Win98 title bar, left of Minimize, once the shell frame
    /// exists. The isolated sandbox scenario has no frame, so the overlay placement remains the
    /// fallback rather than a hard requirement.
    /// </summary>
    private void TryDockHelpButton()
    {
        if (_helpDocked || !GodotObject.IsInstanceValid(_help))
            return;
        if (GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) is not Win98WindowFrame frame ||
            !GodotObject.IsInstanceValid(frame.TitleBarCommands))
        {
            return;
        }

        HBoxContainer row = frame.TitleBarCommands;
        _help.GetParent()?.RemoveChild(_help);
        _help.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _help.OffsetLeft = _help.OffsetTop = _help.OffsetRight = _help.OffsetBottom = 0;
        _help.CustomMinimumSize = new Vector2(20, 18);
        _help.Text = "?";
        row.AddChild(_help);
        // Minimize, Maximize and Close are the last three commands; Help sits just before them.
        row.MoveChild(_help, Math.Max(0, row.GetChildCount() - 4));
        _helpDocked = true;
    }

    private void ToggleContextHelp()
    {
        _helpActive = !_helpActive;
        if (!_helpDocked)
        {
            _help.Text = _helpActive ? "Close Help" : "Help";
            _help.CustomMinimumSize = new Vector2(_helpActive ? 88 : 58, 24);
        }
        _help.TooltipText = _helpActive
            ? "Leave Help mode."
            : "Explain the part of the current screen you hover over.";
        bool work = UseWorkHelpSurface();
        _helpSpotlight.Visible = _helpActive && !work;
        _exitHelp.Visible = _helpActive && !work;
        if (_workHelpSpotlight is not null)
            _workHelpSpotlight.Visible = _helpActive && work;

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
            _workHelpSpotlight?.ClearTarget();
            if (_workHelpPopup is not null)
                _workHelpPopup.Visible = false;
            RefreshHint();
        }
    }

    /// <summary>
    /// Dim the workspace and ring the control the current prompt is talking about. Steps whose
    /// action lives in the world or in the separate Work window resolve to nothing and the
    /// overlay stays off, so the game is never dimmed for a hint that points at no control.
    /// </summary>
    private bool IsOverCommandBar(Vector2 position) =>
        GetTree().Root.FindChild("Win98CommandBar", true, false) is Control bar &&
        bar.IsVisibleInTree() &&
        bar.GetGlobalRect().HasPoint(position);

    private void ClearTutorialSpotlight()
    {
        _tutorialSpotlight.Visible = false;
        HideForeignSpotlight();
        _lockMainViewport = false;
        _lockedTargetRect = new Rect2();
        _lockedAlternateRect = new Rect2();
    }

    private void HideForeignSpotlight()
    {
        if (_activeForeignSpotlight is null)
            return;
        if (GodotObject.IsInstanceValid(_activeForeignSpotlight))
        {
            _activeForeignSpotlight.ClearTarget();
            _activeForeignSpotlight.Visible = false;
        }
        _activeForeignSpotlight = null;
    }

    /// <summary>
    /// Get or build a dim/highlight overlay inside another window — the detached Paint
    /// Background panel is the case that needs it. One overlay per window, kept for reuse.
    /// </summary>
    private HelpSpotlightOverlay? ForeignSpotlightFor(Window window)
    {
        if (_foreignSpotlights.TryGetValue(window.GetInstanceId(), out HelpSpotlightOverlay? existing) &&
            GodotObject.IsInstanceValid(existing))
        {
            return existing;
        }

        var layer = new CanvasLayer { Name = "TutorialForeignSpotlightLayer", Layer = 250 };
        window.AddChild(layer);
        var overlay = new HelpSpotlightOverlay
        {
            Name = "TutorialForeignSpotlight",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            DimAlpha = 0.41f,
        };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(overlay);
        _foreignSpotlights[window.GetInstanceId()] = overlay;
        return overlay;
    }

    private void RefreshTutorialSpotlight()
    {
        Control? target = _helpActive || _displayedStepId is null || !_panel.Visible
            ? null
            : ResolveStepTarget(_displayedStepId);

        if (!GodotObject.IsInstanceValid(target) || !target!.IsVisibleInTree())
        {
            ClearTutorialSpotlight();
            return;
        }

        // A panel the player has floated onto the desktop lives in its own Window. Its rect means
        // nothing in this viewport, so the highlight has to be drawn over there: dim the shell
        // whole, dim the floating window too, and cut the hole around the control in the window
        // that actually contains it.
        if (target.GetViewport() is Window foreign && foreign != GetWindow())
        {
            HelpSpotlightOverlay? overlay = ForeignSpotlightFor(foreign);
            if (overlay is null)
            {
                ClearTutorialSpotlight();
                return;
            }

            Rect2 foreignRect = target.GetGlobalRect().Intersection(foreign.GetVisibleRect());
            if (foreignRect.Size.X <= 1 || foreignRect.Size.Y <= 1)
            {
                ClearTutorialSpotlight();
                return;
            }

            overlay.SetTarget(foreignRect);
            overlay.Visible = true;
            _activeForeignSpotlight = overlay;
            _tutorialSpotlight.ClearTarget();
            _tutorialSpotlight.Visible = true;
            _lockMainViewport = true;
            _lockedTargetRect = new Rect2();
            _lockedAlternateRect = new Rect2();
            return;
        }

        HideForeignSpotlight();
        _lockMainViewport = false;
        Rect2 viewportRect = GetViewport().GetVisibleRect();
        Rect2 rect = target.GetGlobalRect().Intersection(viewportRect);
        if (rect.Size.X <= 1 || rect.Size.Y <= 1)
        {
            ClearTutorialSpotlight();
            return;
        }

        // A step may point at a second place the prompt mentions but does not ask you to click —
        // the credit counter, while the action is the Buy button.
        Control? aside = ResolveStepAside(_displayedStepId!);
        Rect2 asideRect = GodotObject.IsInstanceValid(aside) && aside!.IsVisibleInTree()
            ? aside.GetGlobalRect().Intersection(viewportRect)
            : new Rect2();
        _tutorialSpotlight.SetTargets(asideRect.HasArea() ? [rect, asideRect] : [rect]);
        _tutorialSpotlight.Visible = true;
        _lockedTargetRect = rect;

        Control? alternate = ResolveStepAlternate(_displayedStepId!);
        _lockedAlternateRect = GodotObject.IsInstanceValid(alternate) && alternate!.IsVisibleInTree()
            ? alternate.GetGlobalRect()
            : new Rect2();
    }

    /// <summary>
    /// A control the step's prompt mentions as an alternative route, clickable but not ringed.
    /// Buying in Buddy Studio is the case: the prompt points at Buy and also says a double-click
    /// on the style does the same, so the lock must not swallow that double-click.
    /// </summary>
    /// <summary>
    /// The Paint menu opens as a PopupMenu in its own window, which the viewport-level input lock
    /// cannot reach — so the player could ignore a highlighted "Buddy" and pick "Background"
    /// instead. Grey out whichever entry the current step is not asking for, and restore both as
    /// soon as the walkthrough moves on. The menu rebuilds itself on every popup, so this is
    /// re-applied each frame rather than wired once.
    /// </summary>
    private void RefreshPaintMenuGate()
    {
        if (GetTree().Root.FindChild("Win98PaintCommand", true, false) is not MenuButton paint)
            return;
        PopupMenu popup = paint.GetPopup();
        if (!popup.Visible)
            return;

        string? required = _displayedStepId switch
        {
            TutorialStepIds.OpenPaintBuddy => "Buddy",
            TutorialStepIds.OpenPaintBackground => "Background",
            _ => null,
        };

        for (int index = 0; index < popup.ItemCount; index++)
        {
            // Re-enable rather than skip: leaving a step must hand the menu back intact, not
            // rely on the command bar happening to rebuild the popup later.
            bool gated = required is not null &&
                !string.Equals(popup.GetItemText(index), required, StringComparison.Ordinal);
            popup.SetItemDisabled(index, gated);
        }
    }

    /// <summary>
    /// A second control the prompt draws attention to without asking the player to click it.
    /// The purchase step points at the Buy button and, alongside it, at the credit counter.
    /// </summary>
    private Control? ResolveStepAside(string stepId) => stepId switch
    {
        TutorialStepIds.PurchaseBaseballBat =>
            GetTree().Root.FindChild("Win98BalanceLabel", true, false) as Control,
        // Paint and Save are one lesson, so both are ringed. Save also needs to be clickable,
        // which the aside alone does not grant — see ResolveStepAlternate.
        TutorialStepIds.PaintBuddy when IsPaintBuddyOpen() => _editor!.SaveButton,
        _ => null,
    };

    private Control? ResolveStepAlternate(string stepId) => stepId switch
    {
        TutorialStepIds.BuyStudioItem when IsStudioOpen() => _studio!.CatalogGrid,
        // The step's own target is the canvas; the lock would otherwise swallow the Save click
        // that ends the step.
        TutorialStepIds.PaintBuddy when IsPaintBuddyOpen() => _editor!.SaveButton,
        _ => null,
    };

    private Control? ResolveStepTarget(string stepId)
    {
        switch (stepId)
        {
            case TutorialStepIds.PurchaseBaseballBat when GodotObject.IsInstanceValid(_shop):
                return _shop!.BuyButtonFor(ContentIds.ToolBaseballBat);
            // The host's own NewButton is hidden in the Win98 paint layout and replaced by
            // Win98NewCharacterButton, so pointing at it resolved to an invisible control and the
            // step drew no highlight at all (owner feedback 2026-08-20).
            case TutorialStepIds.CreateBuddy when IsPaintBuddyOpen():
                return FindInEditor("Win98NewCharacterPrompt") is { } prompt && prompt.IsVisibleInTree()
                    ? prompt
                    : CanCreateCharacter()
                        ? FindInEditor("Win98NewCharacterButton")
                        : FindInEditor("CharacterLibraryList");

            // Only the blue bar: that is the part the player has to drag, and ringing the whole
            // panel said nothing about where to grab it.
            case TutorialStepIds.FloatPaintBackgroundPanel when IsBackgroundOpen():
                return GodotObject.IsInstanceValid(_backgroundEditor) &&
                       _backgroundEditor!.FindChild("PaintBackgroundPanel", true, false) is Control panel
                    ? panel.FindChild("TitleBar", true, false) as Control
                    : null;

            case TutorialStepIds.UsePaintedBuddy when IsPaintBuddyOpen():
                return _editor!.UseButton;
            case TutorialStepIds.SaveBuddyStudio when IsStudioOpen():
                return _studio!.SaveAction;

            // Point at the one category button and the one tile, not the whole strip or grid.
            case TutorialStepIds.SelectNoseCategory when IsStudioOpen():
                return _studio!.CategoryStrip.ButtonFor(StudioNoseCategoryId);
            case TutorialStepIds.SelectNoseButtonStyle when IsStudioOpen():
                return _studio!.CatalogGrid.TileFor(CharacterFeatureIds.NoseButton);
            case TutorialStepIds.Farewell:
                return GodotObject.IsInstanceValid(_help) ? _help : null;
        }

        if (!StepSpotlights.TryGetValue(stepId, out SpotlightTarget spotlight))
            return null;

        Node? scope = spotlight.Scope switch
        {
            SpotlightScope.PaintBuddy => _editor,
            SpotlightScope.Background => _backgroundEditor,
            SpotlightScope.Studio => _studio,
            _ => GetTree().Root,
        };
        if (!GodotObject.IsInstanceValid(scope))
            return null;

        var resolved = scope!.FindChild(spotlight.NodeName, true, false) as Control;
        // A step that names a control its own workspace cannot produce is a wiring bug, not a
        // quiet no-op: without this the prompt just silently loses its highlight.
        if (!GodotObject.IsInstanceValid(resolved) && _unresolvedSpotlights.Add(stepId))
        {
            Log.Warn(
                Category,
                $"Tutorial step '{stepId}' found no '{spotlight.NodeName}' under {spotlight.Scope}.");
        }
        return resolved;
    }

    private void RefreshContextHelp()
    {
        // Work Mode hides the shell, so Help runs against the companion's own window instead.
        bool work = UseWorkHelpSurface();
        Viewport viewport = work ? _workView!.GetWindow() : GetViewport();
        HelpSpotlightOverlay spotlight = work ? _workHelpSpotlight! : _helpSpotlight;
        PanelContainer popup = work ? _workHelpPopup! : _helpPopup;
        Label? title = work ? _workHelpTitle : _helpTitle;
        Label body = work ? _workHelpBody! : _helpBody;

        _exitHelp.Visible = !work;
        spotlight.Visible = true;
        if (work)
        {
            _helpSpotlight.Visible = false;
            _helpPopup.Visible = false;
        }
        else if (_workHelpSpotlight is not null)
        {
            _workHelpSpotlight.Visible = false;
            _workHelpPopup!.Visible = false;
        }

        Control? hovered = viewport.GuiGetHoveredControl();
        if (!TryResolveHelp(hovered, out Control? target, out HelpDefinition definition))
        {
            spotlight.ClearTarget();
            popup.Visible = false;
            return;
        }

        Rect2 rect = target!.GetGlobalRect().Intersection(viewport.GetVisibleRect());
        if (rect.Size.X <= 1 || rect.Size.Y <= 1)
        {
            spotlight.ClearTarget();
            popup.Visible = false;
            return;
        }

        spotlight.SetTarget(rect);
        if (title is not null)
            title.Text = definition.Title;
        body.Text = definition.Body;
        PositionHelpPopup(popup, rect, viewport.GetVisibleRect().Size);
        popup.Visible = true;
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

    private static void PositionHelpPopup(PanelContainer popup, Rect2 target, Vector2 viewport)
    {
        const float gap = 10;
        // The Work companion window is far smaller than the shell, so the popup shrinks to fit
        // rather than hanging off its own window.
        float width = Math.Min(340, Math.Max(180, viewport.X - (gap * 2)));
        float height = Math.Min(150, Math.Max(90, viewport.Y - (gap * 2)));
        float x = target.End.X + gap;
        if (x + width > viewport.X)
            x = Math.Max(gap, target.Position.X - width - gap);
        x = Math.Clamp(x, gap, Math.Max(gap, viewport.X - width - gap));
        float y = Math.Clamp(target.Position.Y, gap, Math.Max(gap, viewport.Y - height - gap));
        popup.Position = new Vector2(x, y);
        popup.Size = new Vector2(width, height);
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
        // The character editor pauses the tree; the pulse has to keep running there too.
        public HelpSpotlightOverlay() => ProcessMode = ProcessModeEnum.Always;

        private readonly List<Rect2> _targets = new();

        /// <summary>Help mode dims hard to force focus; the tutorial only nudges the eye.</summary>
        public float DimAlpha { get; init; } = 0.70f;

        /// <summary>
        /// The hole breathes rather than sitting still, so the eye is drawn to it without a
        /// flash. Slow on purpose: a fast pulse reads as an error state.
        /// </summary>
        private const float PulseCenterPixels = 5.0f;
        private const float PulseAmplitudePixels = 4.0f;
        private const float PulseSeconds = 3.2f;

        private double _pulseSeconds;

        private Color Dim => new(0, 0, 0, DimAlpha);

        private float PulseGrow
        {
            get
            {
                // Smoothstep over a ping-pong ramp: the same period as a sine but with a gentler
                // crossing through the middle, where a moving sub-pixel edge shows up most.
                float phase = (float)Mathf.PosMod(_pulseSeconds / PulseSeconds, 1.0);
                float ramp = phase < 0.5f ? phase * 2.0f : (1.0f - phase) * 2.0f;
                float eased = ramp * ramp * (3.0f - (2.0f * ramp));
                return PulseCenterPixels + (PulseAmplitudePixels * ((eased * 2.0f) - 1.0f));
            }
        }

        public override void _Process(double delta)
        {
            // Nothing to breathe when the whole surface is dimmed flat.
            if (_targets.Count == 0 || !IsVisibleInTree())
                return;

            _pulseSeconds += delta;
            QueueRedraw();
        }

        /// <summary>Whole-pixel rect, so every dim band edge lands exactly on a pixel.</summary>
        private static Rect2 RoundToPixels(Rect2 rect)
        {
            Vector2 topLeft = rect.Position.Round();
            Vector2 bottomRight = rect.End.Round();
            return new Rect2(topLeft, bottomRight - topLeft);
        }

        public void SetTarget(Rect2 rect) => SetTargets(rect);

        public void SetTargets(params Rect2[] rects)
        {
            _targets.Clear();
            foreach (Rect2 rect in rects)
                _targets.Add(rect);
            QueueRedraw();
        }

        public void ClearTarget()
        {
            _targets.Clear();
            QueueRedraw();
        }

        /// <summary>
        /// Dim everything except the target rectangles. Painting the complement of one rectangle
        /// is four bands, but a step can point at two places at once (buy this, and here is where
        /// the money lands), so the dim is laid down band by band: split on every target edge,
        /// then within each band fill only the gaps between the targets that span it.
        /// </summary>
        public override void _Draw()
        {
            Rect2 viewport = new(Vector2.Zero, Size);
            if (_targets.Count == 0)
            {
                DrawRect(viewport, Dim, true);
                return;
            }

            var visible = new List<Rect2>(_targets.Count);
            var edges = new SortedSet<float> { 0f, Size.Y };
            // Whole pixels. The dim is painted as bands around the hole, and a band whose edges
            // land mid-pixel leaves a sub-pixel sliver that rasterises as a full-width hairline
            // across the screen — the streaks the pulse appeared to emit (owner report
            // 2026-08-20). Rounding the hole makes every band edge exact.
            float grow = Mathf.Round(PulseGrow);
            foreach (Rect2 candidate in _targets)
            {
                Rect2 clipped = RoundToPixels(candidate.Grow(grow)).Intersection(viewport);
                if (clipped.Size.X <= 0 || clipped.Size.Y <= 0)
                    continue;
                visible.Add(clipped);
                edges.Add(clipped.Position.Y);
                edges.Add(clipped.End.Y);
            }

            if (visible.Count == 0)
            {
                DrawRect(viewport, Dim, true);
                return;
            }

            float[] rows = edges.ToArray();
            for (int index = 0; index + 1 < rows.Length; index++)
            {
                float top = rows[index];
                float bottom = rows[index + 1];
                if (bottom - top < 1.0f)
                    continue;

                var spans = new List<Rect2>();
                foreach (Rect2 rect in visible)
                {
                    if (rect.Position.Y <= top && rect.End.Y >= bottom)
                        spans.Add(rect);
                }
                spans.Sort(static (left, right) => left.Position.X.CompareTo(right.Position.X));

                float cursor = 0f;
                foreach (Rect2 span in spans)
                {
                    if (span.Position.X > cursor)
                        DrawRect(new Rect2(cursor, top, span.Position.X - cursor, bottom - top), Dim, true);
                    cursor = Math.Max(cursor, span.End.X);
                }
                if (cursor < Size.X)
                    DrawRect(new Rect2(cursor, top, Size.X - cursor, bottom - top), Dim, true);
            }

            foreach (Rect2 rect in visible)
                DrawRect(rect, Win98ThemeFactory.Highlight, false, 3, antialiased: true);
        }
    }
}
