using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Integration seam between action-driven onboarding and <see cref="TutorialGuideView"/>. The
/// controller keeps progress, semantic variant selection, spotlighting and input authority; the
/// guide view owns expressive text/reveal/speaking/portrait presentation.
/// </summary>
public partial class FirstSessionGuidanceController
{
    private TutorialSemanticRefreshBridge? _semanticRefreshBridge;
    private TutorialExpressiveBridge? _expressiveBridge;
    private TutorialGuideView? _guideView;
    private Control? _workGuidePortraitHost;
    private string? _semanticPresentationIdentity;
    private string? _presentedCueKey;

    private void InstallExpressiveTextBridge()
    {
        if (!GodotObject.IsInstanceValid(_guideView))
        {
            _guideView = new TutorialGuideView(_characterPresenter)
            {
                Name = nameof(TutorialGuideView),
            };
            AddChild(_guideView);
        }

        if (!GodotObject.IsInstanceValid(_semanticRefreshBridge))
        {
            _semanticRefreshBridge = new TutorialSemanticRefreshBridge(this)
            {
                Name = nameof(TutorialSemanticRefreshBridge),
                ProcessMode = ProcessModeEnum.Always,
                // Run before the controller's default priority so a variant change is noticed in
                // the same frame the controller renders it, not one frame late.
                ProcessPriority = -100,
            };
            AddChild(_semanticRefreshBridge);
        }

        if (GodotObject.IsInstanceValid(_expressiveBridge))
            return;

        _expressiveBridge = new TutorialExpressiveBridge(this)
        {
            Name = nameof(TutorialExpressiveBridge),
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_expressiveBridge);
    }

    /// <summary>
    /// Semantic step+variant identity is the sole refresh authority for conditional tutorial copy;
    /// the pre-feature rendered-text guard it replaced has been removed. Identity covers the live
    /// Drop Tool binding, so a rebind re-renders the prompt without depending on English
    /// source-string changes.
    /// </summary>
    internal void PrepareSemanticTutorialRefresh()
    {
        if (_tutorial is null)
            return;

        string? stepId = _tutorial.NextIncompleteStepId;
        string? identity = stepId is null ? null : ResolveTutorialPresentationIdentity(stepId);
        if (stepId is not null &&
            string.Equals(stepId, _displayedStepId, StringComparison.Ordinal) &&
            !string.Equals(identity, _semanticPresentationIdentity, StringComparison.Ordinal))
        {
            RefreshHint();
        }

        _semanticPresentationIdentity = identity;
    }

    /// <summary>Publishes the current semantic guide cue into the active main/Work host.</summary>
    internal void SyncExpressiveTutorialPresentation()
    {
        if (!GodotObject.IsInstanceValid(_body) ||
            !GodotObject.IsInstanceValid(_panel) ||
            !GodotObject.IsInstanceValid(_guideView))
        {
            return;
        }

        string? stepId = _displayedStepId;
        if (_helpActive || stepId is null)
        {
            _guideView!.Hide();
            // Hide only clears visibility, so the next Present must actually run rather than
            // being short-circuited by a cue key left over from before the guide was hidden.
            _presentedCueKey = null;
            if (GodotObject.IsInstanceValid(_dismiss))
                _dismiss.Disabled = false;
            return;
        }

        _semanticPresentationIdentity = ResolveTutorialPresentationIdentity(stepId);
        LocalSettingsSave settings = _sandbox.Shell.CurrentLocalSettings;
        string dropBinding = LocalSettingsInputBindings.DropTool(settings);

        bool useWorkSurface = IsWorkTutorialStep(stepId) &&
                              IsWorkActive() &&
                              GodotObject.IsInstanceValid(_workGuideWindow) &&
                              GodotObject.IsInstanceValid(_workGuideBody) &&
                              _workGuideWindow!.Visible;

        // Presenting is idempotent, but formatting the line to discover that is not: this runs
        // every frame for the whole tutorial. The cue key is the cheap comparison that decides
        // whether the prose is worth rebuilding at all. Motion is part of the key because the
        // animated/static tag choice is baked in when the line is presented.
        string cueKey = string.Concat(
            useWorkSurface ? "work:" : "main:",
            _semanticPresentationIdentity,
            Win98MotionPolicy.Allows(settings) ? ":motion" : ":still");
        if (string.Equals(cueKey, _presentedCueKey, StringComparison.Ordinal))
        {
            UpdateDismissGate();
            return;
        }
        _presentedCueKey = cueKey;

        string semantic = TutorialExpressiveCopy.TryFormat(stepId, dropBinding, out string authored)
            ? authored
            : TextFor(stepId);

        if (useWorkSurface)
        {
            _guideView!.PresentWork(
                _workGuideBody!,
                EnsureWorkGuidePortraitHost(),
                cueKey,
                stepId,
                semantic,
                settings);
        }
        else
        {
            _guideView!.PresentMain(_body, cueKey, semantic, settings);
        }

        UpdateDismissGate();
    }

    /// <summary>
    /// Continue/Goodbye must never turn the same click that finishes a line into progression.
    /// </summary>
    private void UpdateDismissGate()
    {
        if (!GodotObject.IsInstanceValid(_dismiss))
            return;
        _dismiss.Disabled = _dismiss.Visible && _guideView!.MainIsRevealing;
    }

    private string ResolveTutorialPresentationIdentity(string stepId)
    {
        string variant = stepId switch
        {
            TutorialStepIds.CreateBuddy => CanCreateCharacter() ? "can-create" : "select-existing",
            TutorialStepIds.ExitBuddyStudio => _studioNothingToSave ? "nothing-to-save" : "saved-item",
            TutorialStepIds.UnequipTool =>
                "drop-" + LocalSettingsInputBindings.DropTool(_sandbox.Shell.CurrentLocalSettings),
            _ => "default",
        };
        return $"{stepId}:{variant}";
    }

    internal bool CompleteMainExpressiveRevealAt(Vector2 viewportPosition)
    {
        if (!GodotObject.IsInstanceValid(_panel) || !_panel.Visible ||
            !_panel.GetGlobalRect().HasPoint(viewportPosition) ||
            !GodotObject.IsInstanceValid(_guideView))
        {
            return false;
        }

        bool completed = _guideView!.CompleteMainReveal();
        if (completed && GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
        return completed;
    }

    private Control EnsureWorkGuidePortraitHost()
    {
        if (GodotObject.IsInstanceValid(_workGuidePortraitHost))
            return _workGuidePortraitHost!;
        if (!GodotObject.IsInstanceValid(_workGuideBody) ||
            _workGuideBody!.GetParent()?.GetParent() is not HBoxContainer split)
        {
            throw new InvalidOperationException("Work tutorial guide split was not composed.");
        }

        _workGuidePortraitHost = new Control
        {
            Name = "TutorialWorkGuideSlot",
            CustomMinimumSize = new Vector2(92, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        split.AddChild(_workGuidePortraitHost);
        return _workGuidePortraitHost;
    }
}

/// <summary>Pre-controller semantic variant refresh; see PrepareSemanticTutorialRefresh.</summary>
internal sealed partial class TutorialSemanticRefreshBridge : Node
{
    private readonly FirstSessionGuidanceController _owner;

    public TutorialSemanticRefreshBridge(FirstSessionGuidanceController owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public override void _Process(double delta)
    {
        _ = delta;
        if (GodotObject.IsInstanceValid(_owner))
            _owner.PrepareSemanticTutorialRefresh();
    }
}

/// <summary>
/// Runs after its parent controller in normal tree order and catches clicks on empty parts of the
/// main tutorial frame so the first click completes the typewriter. The guide view owns the text
/// control's own click-to-complete path in both main and Work hosts.
/// </summary>
internal sealed partial class TutorialExpressiveBridge : Node
{
    private readonly FirstSessionGuidanceController _owner;

    public TutorialExpressiveBridge(FirstSessionGuidanceController owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public override void _Process(double delta)
    {
        _ = delta;
        if (GodotObject.IsInstanceValid(_owner))
            _owner.SyncExpressiveTutorialPresentation();
    }

    public override void _Input(InputEvent input)
    {
        if (!GodotObject.IsInstanceValid(_owner) ||
            input is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            } mouse)
        {
            return;
        }

        if (_owner.CompleteMainExpressiveRevealAt(mouse.Position))
            GetViewport().SetInputAsHandled();
    }
}
