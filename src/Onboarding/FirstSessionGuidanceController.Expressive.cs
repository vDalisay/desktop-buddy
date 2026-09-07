using System;
using DesktopBuddy.Domain.Persistence;
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
                // Run before the controller's default priority so the old rendered-text guard is
                // made inert before FirstSessionGuidanceController._Process evaluates it.
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
    /// Semantic step+variant identity is the refresh authority for conditional tutorial copy.
    /// The controller still contains its pre-feature rendered-text guard, but this pass mirrors the
    /// current source into that legacy field before the controller processes, so text equality can
    /// no longer decide whether a variant re-renders. This also makes a live Drop Tool rebind a
    /// semantic variant rather than depending on English source-string changes.
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
        // Neutralize the legacy text-equality refresh path. Step changes are still handled by the
        // controller's step-id comparison; live variants are handled by identity above.
        _lastRenderedText = stepId is null ? null : TextFor(stepId);
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

        if (useWorkSurface)
        {
            string semantic = TutorialExpressiveCopy.Format(stepId, _workGuideBody!.Text, dropBinding);
            _guideView!.PresentWork(
                _workGuideBody,
                EnsureWorkGuidePortraitHost(),
                $"work:{_semanticPresentationIdentity}",
                stepId,
                semantic,
                settings);
        }
        else
        {
            string semantic = TutorialExpressiveCopy.Format(stepId, _body.Text, dropBinding);
            _guideView!.PresentMain(
                _body,
                $"main:{_semanticPresentationIdentity}",
                semantic,
                settings);
        }

        // Continue/Goodbye must never turn the same click that finishes a line into progression.
        if (GodotObject.IsInstanceValid(_dismiss) && _dismiss.Visible)
            _dismiss.Disabled = _guideView!.MainIsRevealing;
        else if (GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
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
