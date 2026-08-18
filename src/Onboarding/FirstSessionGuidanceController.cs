using System;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Shop;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Optional seam for the later tutorial-character pass. The text flow is complete without an
/// implementation; a character presenter can mirror the same stable step IDs without owning
/// tutorial authority or persistence.
/// </summary>
public interface ITutorialCharacterPresenter
{
    void Present(string stepId, string text);
    void Dismiss();
}

/// <summary>
/// Lightweight first-session guidance. It observes real runtime state rather than intercepting
/// player input: grabbing Buddy, earned credits, the visible Inventory, a successful purchase,
/// Paint Buddy, and Work Mode all advance the durable record. The only mouse-stopping area is the
/// small hint panel itself; the rest of the overlay is click-through.
/// </summary>
public partial class FirstSessionGuidanceController : CanvasLayer
{
    private const string Category = "Onboarding";

    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private TutorialProgressState _tutorial = null!;
    private ITutorialCharacterPresenter? _characterPresenter;

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _body = null!;
    private Button _dismiss = null!;
    private Button _skip = null!;

    private CharacterEditorHost? _editor;
    private ShopPanel? _shop;
    private WorkCompanionCoordinator? _work;
    private bool _wasGrabbing;
    private bool _wasWorkActive;
    private bool _hasSeenWorkActive;
    private string? _displayedStepId;
    private string? _dismissedStepId;

    public TutorialProgressState Progress => _tutorial;
    public string? DisplayedStepId => _displayedStepId;

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
        Layer = 120;
    }

    public override void _Ready()
    {
        BuildUi();
        _context.Progress.Changed += OnProgressChanged;

        // Existing players should not suddenly receive a "first session" walkthrough merely
        // because the Demo build learned one. Reset Progress clears the extension record and the
        // same controller will then naturally begin at Grab Buddy.
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
        bool grabbing = _sandbox.Grab.IsGrabbing;
        if (grabbing && !_wasGrabbing && _sandbox.Grab.CurrentGrab.Target is PuppetPartBody)
            Complete(TutorialStepIds.GrabBuddy);
        _wasGrabbing = grabbing;

        _editor ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _shop ??= GetTree().Root.FindChild("ShopPanel", true, false) as ShopPanel;
        _work ??= GetTree().Root.FindChild(nameof(WorkCompanionCoordinator), true, false) as WorkCompanionCoordinator;

        if (GodotObject.IsInstanceValid(_shop) && _shop!.IsVisibleInTree())
            Complete(TutorialStepIds.OpenShop);

        if (GodotObject.IsInstanceValid(_editor) && _editor!.IsEditorOpen)
            Complete(TutorialStepIds.OpenPaintBuddy);

        bool workActive = GodotObject.IsInstanceValid(_work) && _work!.IsActive;
        if (workActive && !_wasWorkActive)
        {
            _hasSeenWorkActive = true;
            Complete(TutorialStepIds.EnterWorkMode);
        }
        else if (!workActive && _wasWorkActive && _hasSeenWorkActive)
        {
            Complete(TutorialStepIds.ExitWorkMode);
        }
        _wasWorkActive = workActive;

        // Reset Progress replaces Extensions in place and deliberately raises no semantic event.
        // This cheap comparison makes the first-session panel return immediately after that reset.
        string? next = _tutorial.NextIncompleteStepId;
        if (!string.Equals(next, _displayedStepId, StringComparison.Ordinal) &&
            !string.Equals(next, _dismissedStepId, StringComparison.Ordinal))
        {
            RefreshHint();
        }
    }

    public override void _ExitTree()
    {
        if (_context?.Progress is not null)
            _context.Progress.Changed -= OnProgressChanged;
        _characterPresenter?.Dismiss();
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
        margin.OffsetTop = -154;
        margin.OffsetRight = 352;
        margin.OffsetBottom = -12;
        _root.AddChild(margin);

        _panel = new PanelContainer
        {
            Name = "FirstSessionGuidancePanel",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(320, 118),
            Theme = Win98ThemeFactory.Create(),
        };
        _panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        margin.AddChild(_panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(column);

        _title = new Label
        {
            Text = "Desktop Buddy Help",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _title.AddThemeColorOverride("font_color", Win98ThemeFactory.ActiveTitle);
        column.AddChild(_title);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(_body);

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        actions.AddThemeConstantOverride("separation", 6);
        column.AddChild(actions);

        _dismiss = new Button
        {
            Text = "Dismiss",
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Hide this hint. Tutorial progress is not skipped.",
        };
        _dismiss.Pressed += DismissCurrent;
        actions.AddChild(_dismiss);

        _skip = new Button
        {
            Text = "Skip Tutorial",
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Mark the remaining first-session hints complete and stop showing the tutorial.",
        };
        _skip.Pressed += () => SkipTutorial();
        actions.AddChild(_skip);
    }

    private void OnProgressChanged(ProgressChange change)
    {
        if (change == ProgressChange.BalanceChanged)
            Complete(TutorialStepIds.EarnCredits);
        else if (change == ProgressChange.ContentPurchased)
            Complete(TutorialStepIds.PurchaseContent);
    }

    private void Complete(string stepId)
    {
        if (!_tutorial.MarkCompleted(stepId))
            return;
        _dismissedStepId = null;
        RequestImmediateFlush();
        RefreshHint();
    }

    private void DismissCurrent()
    {
        _dismissedStepId = _displayedStepId;
        _displayedStepId = null;
        _panel.Visible = false;
        _characterPresenter?.Dismiss();
    }

    private void RefreshHint()
    {
        string? stepId = _tutorial.NextIncompleteStepId;
        _displayedStepId = stepId;
        if (stepId is null || string.Equals(stepId, _dismissedStepId, StringComparison.Ordinal))
        {
            _panel.Visible = false;
            _characterPresenter?.Dismiss();
            return;
        }

        string text = TextFor(stepId);
        _body.Text = text;
        _panel.Visible = true;
        _characterPresenter?.Present(stepId, text);
    }

    private static string TextFor(string stepId) => stepId switch
    {
        TutorialStepIds.GrabBuddy =>
            "Start with Grab. Click and drag any part of Buddy to pick them up and move them around.",
        TutorialStepIds.EarnCredits =>
            "Playing with Buddy earns credits. Tool impacts, care and other rewarded actions add to the balance in the top bar.",
        TutorialStepIds.OpenShop =>
            "Open Inventory in the top bar when you want a new tool. You can save your credits for whichever purchasable tool you want.",
        TutorialStepIds.PurchaseContent =>
            "Buy an item you can afford. Owned tools stay in Inventory and change to Equip instead of Buy.",
        TutorialStepIds.OpenPaintBuddy =>
            "Paint Buddy is free. Open Paint ▸ Buddy to draw directly on your character with the full paint toolset.",
        TutorialStepIds.EnterWorkMode =>
            "Try Work Mode from the top bar. It turns Buddy into a small desktop companion and counts your keyboard and mouse actions locally.",
        TutorialStepIds.ExitWorkMode =>
            "While Work Mode is active, double-click Buddy to return to normal Play Mode.",
        _ => string.Empty,
    };

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
}